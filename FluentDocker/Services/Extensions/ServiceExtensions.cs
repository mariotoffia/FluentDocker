using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Model.Containers;
using FluentDocker.Model.Drivers;
using FluentDocker.Services.Impl;

namespace FluentDocker.Services.Extensions
{
  /// <summary>
  /// Extension methods for V3 service interfaces.
  /// </summary>
  public static partial class ServiceExtensions
  {
    // Minimum per-attempt TCP connect deadline (ms); poll interval governs cadence only.
    private const int MinConnectBudgetMs = 2000;
    private const int LogTailLines = 100;

    #region Container Extensions

    /// <summary>
    /// Gets the container configuration (by inspecting the container).
    /// </summary>
    /// <param name="service">The container service.</param>
    /// <param name="fresh">If true, forces a fresh inspection from Docker.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The container configuration.</returns>
    public static async Task<Container> GetConfigurationAsync(
        this IContainerService service,
        bool fresh = false,
        CancellationToken cancellationToken = default)
    {
      if (fresh && service is ContainerService containerService)
        containerService.InvalidateInspectCache();

      return await service.InspectAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the host-exposed endpoint for a container port.
    /// </summary>
    /// <param name="service">The container service.</param>
    /// <param name="portAndProto">Port and protocol, e.g., "5432/tcp".</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The IP endpoint accessible from the host.</returns>
    public static async Task<IPEndPoint> ToHostExposedEndpointAsync(
        this IContainerService service,
        string portAndProto,
        CancellationToken cancellationToken = default)
    {
      return await service.ToHostExposedEndpointAsync(portAndProto, cancellationToken)
          .ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the host port for a container port.
    /// </summary>
    /// <param name="service">The container service.</param>
    /// <param name="portAndProto">Port and protocol, e.g., "5432/tcp".</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The host port number.</returns>
    public static async Task<int> GetHostPortAsync(
        this IContainerService service,
        string portAndProto,
        CancellationToken cancellationToken = default)
    {
      return await service.GetHostPortAsync(portAndProto, cancellationToken)
          .ConfigureAwait(false);
    }

    /// <summary>
    /// Waits for a port to become available asynchronously.
    /// </summary>
    /// <param name="service">The container service.</param>
    /// <param name="portAndProto">Port and protocol, e.g., "5432/tcp".</param>
    /// <param name="timeout">Timeout in milliseconds.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the port is available, false if timeout.</returns>
    /// <remarks>
    /// Extension waits return false on timeout; builder waits wrap false results in
    /// <see cref="FluentDockerException"/>. A TCP connect to a published port only
    /// proves Docker's default proxy accepts it, not that the app inside is ready.
    /// Fails fast when the container reaches a terminal state (exited/dead) before the
    /// port is ready: throws <see cref="FluentDockerException"/> with the exit code and a
    /// log tail rather than polling to timeout.
    /// </remarks>
    public static async Task<bool> WaitForPortAsync(
        this IContainerService service,
        string portAndProto,
        long timeout = 30000,
        CancellationToken cancellationToken = default)
    {
      return await WaitForPortAsync(service, portAndProto, timeout, 100, cancellationToken)
          .ConfigureAwait(false);
    }

    /// <summary>
    /// Waits for a container port, polling at the specified interval.
    /// </summary>
    /// <param name="service">The container service.</param>
    /// <param name="portAndProto">Port and protocol, e.g., "5432/tcp".</param>
    /// <param name="timeout">Timeout in milliseconds.</param>
    /// <param name="pollIntervalMs">Milliseconds to wait between readiness probes.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the port is available, false if timeout.</returns>
    /// <remarks>
    /// A successful TCP connect to a published port can be a false positive: Docker's
    /// userland-proxy on default installs may accept before the app inside is ready.
    /// </remarks>
    public static async Task<bool> WaitForPortAsync(
        this IContainerService service,
        string portAndProto,
        long timeout,
        int pollIntervalMs,
        CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      var sw = Stopwatch.StartNew();
      var forceFreshEndpoint = true;
      Exception? lastException = null;
      while (sw.ElapsedMilliseconds < timeout && !cancellationToken.IsCancellationRequested)
      {
        // Fail fast on a dead container instead of burning the rest of the timeout (outside the
        // catches below so the diagnostic exception is never mistaken for a transient failure).
        await WaitDiagnostics.ThrowIfTerminalAsync(service, cancellationToken).ConfigureAwait(false);
        try
        {
          if (forceFreshEndpoint)
            InvalidateInspectCache(service);

          var endpoint = await service.ToHostExposedEndpointAsync(portAndProto, cancellationToken)
              .ConfigureAwait(false);
          forceFreshEndpoint = endpoint == null;
          // Give the inner connect loop a real handshake budget, not just one poll interval.
          var connectBudget = Math.Min(
              Math.Max(1, timeout - sw.ElapsedMilliseconds),
              Math.Max(pollIntervalMs, MinConnectBudgetMs));
          if (endpoint != null &&
              await WaitForPortAsync(endpoint.Address.ToString(), endpoint.Port, connectBudget, pollIntervalMs, cancellationToken)
                  .ConfigureAwait(false))
          {
            return true;
          }
        }
        catch (DriverException ex) when (ex.IsTransient)
        {
          lastException = ex;
          forceFreshEndpoint = true;
        }
        catch (SocketException ex)
        {
          lastException = ex;
          forceFreshEndpoint = true;
          LogDebug(service, ex, "WaitForPortAsync", portAndProto);
        }

        var delay = (int)Math.Min(pollIntervalMs, Math.Max(1, timeout - sw.ElapsedMilliseconds));
        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
      }

      cancellationToken.ThrowIfCancellationRequested();
      LogWaitFailure(service, lastException, "WaitForPortAsync", portAndProto);
      return false;
    }

    /// <summary>
    /// Waits for a port to become available asynchronously.
    /// </summary>
    /// <param name="host">Host address.</param>
    /// <param name="port">Port number.</param>
    /// <param name="timeout">Timeout in milliseconds.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the port is available, false if timeout.</returns>
    public static async Task<bool> WaitForPortAsync(
        string host,
        int port,
        long timeout = 30000,
        CancellationToken cancellationToken = default)
    {
      return await WaitForPortAsync(host, port, timeout, 100, cancellationToken)
          .ConfigureAwait(false);
    }

    /// <summary>
    /// Waits for a host port, polling at the specified interval.
    /// </summary>
    /// <param name="host">Host address.</param>
    /// <param name="port">Port number.</param>
    /// <param name="timeout">Timeout in milliseconds.</param>
    /// <param name="pollIntervalMs">Milliseconds to wait between TCP connect attempts.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the port is available, false if timeout.</returns>
    public static async Task<bool> WaitForPortAsync(
        string host,
        int port,
        long timeout,
        int pollIntervalMs,
        CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      var sw = Stopwatch.StartNew();

      while (sw.ElapsedMilliseconds < timeout)
      {
        try
        {
          using var client = new TcpClient();
          using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
          var remaining = Math.Max(1, timeout - sw.ElapsedMilliseconds);
          // A connect attempt needs a real handshake budget: capping it at the poll interval
          // (100 ms default) would abort every attempt against a remote daemon before the
          // SYN-ACK arrives. Floor the per-attempt deadline; polling cadence is separate.
          attemptCts.CancelAfter(TimeSpan.FromMilliseconds(
              Math.Min(remaining, Math.Max(pollIntervalMs, MinConnectBudgetMs))));
          await client.ConnectAsync(host, port, attemptCts.Token).ConfigureAwait(false);
          if (client.Connected)
            return true;
        }
        catch (SocketException)
        {
          // Connection refused - port not ready yet
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
          throw;
        }
        catch (OperationCanceledException)
        {
          // Per-attempt timeout - port not ready yet
        }

        var delay = (int)Math.Min(pollIntervalMs, Math.Max(1, timeout - sw.ElapsedMilliseconds));
        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
      }

      cancellationToken.ThrowIfCancellationRequested();
      return false;
    }

    /// <summary>
    /// Waits for a process to be running inside the container.
    /// </summary>
    /// <param name="service">The container service.</param>
    /// <param name="processName">Name of the process to wait for.</param>
    /// <param name="timeout">Timeout in milliseconds.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the process is running, false if timeout.</returns>
    /// <remarks>
    /// Uses <c>pgrep -f</c> inside the container; minimal images may not include it.
    /// Non-transient driver errors are thrown immediately, except exec failures caused by the
    /// container being momentarily not running (mid-restart), which are retried. Fails fast
    /// with a diagnostic <see cref="FluentDockerException"/> (exit code + log tail) when the
    /// container reaches a terminal state, matching the other wait extensions.
    /// </remarks>
    public static async Task<bool> WaitForProcessAsync(
        this IContainerService service,
        string processName,
        long timeout = 30000,
        CancellationToken cancellationToken = default)
    {
      return await WaitForProcessAsync(service, processName, timeout, 500, cancellationToken)
          .ConfigureAwait(false);
    }

    /// <summary>
    /// Waits for a process in the container, polling at the specified interval.
    /// </summary>
    /// <param name="service">The container service.</param>
    /// <param name="processName">Name of the process to wait for.</param>
    /// <param name="timeout">Timeout in milliseconds.</param>
    /// <param name="pollIntervalMs">Milliseconds to wait between process checks.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the process is running, false if timeout.</returns>
    public static async Task<bool> WaitForProcessAsync(
        this IContainerService service,
        string processName,
        long timeout,
        int pollIntervalMs,
        CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      var sw = Stopwatch.StartNew();

      while (sw.ElapsedMilliseconds < timeout && !cancellationToken.IsCancellationRequested)
      {
        // Fail fast on a dead container instead of burning the rest of the timeout (outside the
        // catches below so the diagnostic exception is never mistaken for a transient failure) —
        // same contract as WaitForPortAsync/WaitForHttpAsync/WaitForLogMessageAsync.
        await WaitDiagnostics.ThrowIfTerminalAsync(service, cancellationToken).ConfigureAwait(false);
        try
        {
          // Uses pgrep inside the container; distroless/scratch images often lack it.
          var result = await service.ExecuteAsync(["pgrep", "-f", processName], cancellationToken)
              .ConfigureAwait(false);

          if (!string.IsNullOrWhiteSpace(result))
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
          throw;
        }
        catch (DriverException ex) when (ex.IsTransient || IsExecAgainstStoppedContainer(ex))
        {
          // An exec failing because the container is momentarily "not running" (mid-restart)
          // is retriable: the terminal check above throws once the container is genuinely dead,
          // so this cannot loop past a real exit.
          LogDebug(service, ex, "WaitForProcessAsync", processName);
        }
        catch (Exception ex) when (IsRetriableWaitException(ex, cancellationToken))
        {
          LogDebug(service, ex, "WaitForProcessAsync", processName);
        }

        await Task.Delay(pollIntervalMs, cancellationToken).ConfigureAwait(false);
      }

      cancellationToken.ThrowIfCancellationRequested();
      return false;
    }

    /// <summary>
    /// True when an exec-class failure is caused by the container not being in a runnable
    /// state RIGHT NOW ("is not running"-style daemon messages, e.g. mid-restart). Such
    /// failures are retriable inside wait loops because the terminal-state check converts a
    /// genuinely dead container into a diagnostic failure on the next poll. Other
    /// non-transient exec failures (bad command, missing pgrep) still rethrow immediately.
    /// </summary>
    private static bool IsExecAgainstStoppedContainer(DriverException ex) =>
        ex.Message.Contains("not running", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("container state improper", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Waits for a HTTP endpoint to return a successful response.
    /// </summary>
    /// <param name="service">The container service.</param>
    /// <param name="portAndProto">Port and protocol, e.g., "8080/tcp".</param>
    /// <param name="path">URL path, e.g., "/health".</param>
    /// <param name="timeout">Timeout in milliseconds.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="useHttps">True to probe with HTTPS; defaults to HTTP.</param>
    /// <returns>True if the endpoint responds successfully, false if timeout.</returns>
    /// <remarks>
    /// Extension waits return false on timeout and throw cancellation or non-transient
    /// driver errors. Builder waits throw <see cref="FluentDockerException"/>. Fails fast
    /// when the container reaches a terminal state (exited/dead) before the endpoint is
    /// ready: throws <see cref="FluentDockerException"/> with the exit code and a log tail
    /// rather than polling to timeout.
    /// </remarks>
    [SuppressMessage("Design", "CA1068:CancellationToken parameters must come last",
        Justification = "Keeps existing positional CancellationToken calls source-compatible.")]
    public static async Task<bool> WaitForHttpAsync(
        this IContainerService service,
        string portAndProto,
        string path = "/",
        long timeout = 30000,
        CancellationToken cancellationToken = default,
        bool useHttps = false)
    {
      return await WaitForHttpAsync(service, portAndProto, path, timeout, 500, cancellationToken, useHttps)
          .ConfigureAwait(false);
    }

    /// <summary>
    /// Waits for an HTTP or HTTPS endpoint, polling at the specified interval.
    /// </summary>
    /// <param name="service">The container service.</param>
    /// <param name="portAndProto">Port and protocol, e.g., "8080/tcp".</param>
    /// <param name="path">URL path, e.g., "/health".</param>
    /// <param name="timeout">Timeout in milliseconds.</param>
    /// <param name="pollIntervalMs">Milliseconds to wait between HTTP probes.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="useHttps">True to probe with HTTPS and readiness-only certificate bypass.</param>
    /// <returns>True if the endpoint responds successfully, false if timeout.</returns>
    [SuppressMessage("Design", "CA1068:CancellationToken parameters must come last",
        Justification = "Keeps existing positional CancellationToken calls source-compatible.")]
    public static async Task<bool> WaitForHttpAsync(
        this IContainerService service,
        string portAndProto,
        string path,
        long timeout,
        int pollIntervalMs,
        CancellationToken cancellationToken = default,
        bool useHttps = false)
    {
      cancellationToken.ThrowIfCancellationRequested();
      var sw = Stopwatch.StartNew();
      var forceFreshEndpoint = true;
      Exception? lastException = null;

      while (sw.ElapsedMilliseconds < timeout && !cancellationToken.IsCancellationRequested)
      {
        // Fail fast on a dead container instead of burning the rest of the timeout (outside the
        // catches below so the diagnostic exception is never mistaken for a transient failure).
        await WaitDiagnostics.ThrowIfTerminalAsync(service, cancellationToken).ConfigureAwait(false);
        try
        {
          if (forceFreshEndpoint)
            InvalidateInspectCache(service);

          var endpoint = await service.ToHostExposedEndpointAsync(portAndProto, cancellationToken)
              .ConfigureAwait(false);
          forceFreshEndpoint = endpoint == null;
          if (endpoint == null)
          {
            await Task.Delay(pollIntervalMs, cancellationToken).ConfigureAwait(false);
            continue;
          }

          var url = new UriBuilder(useHttps ? "https" : "http", endpoint.Address.ToString(), endpoint.Port, path).Uri;
          using var requestCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
          var remainingMs = Math.Max(100, timeout - sw.ElapsedMilliseconds);
          requestCts.CancelAfter(TimeSpan.FromMilliseconds(remainingMs));

          var client = useHttps
              ? SharedHttpClient.InsecureHttpsProbe
              : SharedHttpClient.Instance;
          using var response = await client.GetAsync(url, requestCts.Token).ConfigureAwait(false);
          if (response.IsSuccessStatusCode)
            return true;
        }
        catch (HttpRequestException ex)
        {
          lastException = ex;
          // Not ready yet
        }
        catch (TaskCanceledException ex)
        {
          if (cancellationToken.IsCancellationRequested)
            throw;
          lastException = ex;
          // Timeout on request
        }
        catch (DriverException ex) when (ex.IsTransient)
        {
          lastException = ex;
          forceFreshEndpoint = true;
        }
        catch (SocketException ex)
        {
          lastException = ex;
          forceFreshEndpoint = true;
          LogDebug(service, ex, "WaitForHttpAsync", portAndProto);
        }

        await Task.Delay(pollIntervalMs, cancellationToken).ConfigureAwait(false);
      }

      cancellationToken.ThrowIfCancellationRequested();
      LogWaitFailure(service, lastException, "WaitForHttpAsync", portAndProto);
      return false;
    }

    #endregion
  }
}
