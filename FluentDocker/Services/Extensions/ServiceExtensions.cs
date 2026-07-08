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
using FluentDocker.Services.Impl;
using Microsoft.Extensions.Logging;

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
    /// Extension waits return false on timeout. Builder waits wrap false results in
    /// <see cref="FluentDockerException"/> and may include a container log tail.
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
      while (sw.ElapsedMilliseconds < timeout && !cancellationToken.IsCancellationRequested)
      {
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
          forceFreshEndpoint = true;
        }

        var delay = (int)Math.Min(pollIntervalMs, Math.Max(1, timeout - sw.ElapsedMilliseconds));
        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
      }

      cancellationToken.ThrowIfCancellationRequested();
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
    /// Non-transient driver errors are thrown immediately.
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
        catch (DriverException ex) when (ex.IsTransient)
        {
        }
        catch (Exception ex) when (IsRetriableWaitException(ex))
        {
          LogDebug(service, ex, "WaitForProcessAsync", processName);
        }

        await Task.Delay(pollIntervalMs, cancellationToken).ConfigureAwait(false);
      }

      cancellationToken.ThrowIfCancellationRequested();
      return false;
    }

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
    /// driver errors. Builder waits throw <see cref="FluentDockerException"/>.
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

      while (sw.ElapsedMilliseconds < timeout && !cancellationToken.IsCancellationRequested)
      {
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

          using var response = await Common.SharedHttpClient.Instance.GetAsync(url, requestCts.Token).ConfigureAwait(false);
          if (response.IsSuccessStatusCode)
            return true;
        }
        catch (HttpRequestException)
        {
          // Not ready yet
        }
        catch (TaskCanceledException)
        {
          if (cancellationToken.IsCancellationRequested)
            throw;
          // Timeout on request
        }
        catch (DriverException ex) when (ex.IsTransient)
        {
          forceFreshEndpoint = true;
        }

        await Task.Delay(pollIntervalMs, cancellationToken).ConfigureAwait(false);
      }

      cancellationToken.ThrowIfCancellationRequested();
      return false;
    }

    /// <summary>
    /// Waits for container logs to contain specific text.
    /// </summary>
    /// <param name="service">The container service.</param>
    /// <param name="text">Text to search for in logs.</param>
    /// <param name="timeout">Timeout in milliseconds.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the text was found, false if timeout.</returns>
    /// <remarks>
    /// Extension waits return false on timeout and throw cancellation or non-transient
    /// driver errors. Builder waits throw <see cref="FluentDockerException"/>.
    /// </remarks>
    public static async Task<bool> WaitForLogMessageAsync(
        this IContainerService service,
        string text,
        long timeout = 30000,
        CancellationToken cancellationToken = default)
    {
      return await WaitForLogMessageAsync(service, text, timeout, 500, cancellationToken)
          .ConfigureAwait(false);
    }

    public static async Task<bool> WaitForLogMessageAsync(
        this IContainerService service,
        string text,
        long timeout,
        int pollIntervalMs,
        CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      var sw = Stopwatch.StartNew();
      var firstLogPoll = true;

      while (sw.ElapsedMilliseconds < timeout && !cancellationToken.IsCancellationRequested)
      {
        try
        {
          var logs = service is ContainerService containerService && !firstLogPoll
              ? await containerService.GetLogsTailAsync(LogTailLines, cancellationToken).ConfigureAwait(false)
              : await service.GetLogsAsync(false, cancellationToken).ConfigureAwait(false);
          firstLogPoll = false;
          if (logs?.Contains(text) == true)
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
          throw;
        }
        catch (DriverException ex) when (ex.IsTransient)
        {
        }
        catch (Exception ex) when (IsRetriableWaitException(ex))
        {
          LogDebug(service, ex, "WaitForLogMessageAsync", text);
        }

        await Task.Delay(pollIntervalMs, cancellationToken).ConfigureAwait(false);
      }

      cancellationToken.ThrowIfCancellationRequested();
      return service is ContainerService &&
          await ContainsLogMessageAsync(service, text, cancellationToken).ConfigureAwait(false);
    }

    private static bool IsRetriableWaitException(Exception ex) =>
        ex is not DriverException and not OperationCanceledException and not ObjectDisposedException and not NullReferenceException;

    private static void InvalidateInspectCache(IContainerService service)
    {
      if (service is ContainerService containerService)
        containerService.InvalidateInspectCache();
    }

    private static void LogDebug(IContainerService service, Exception exception, string operation, string value)
    {
      if (service is not ContainerService containerService)
        return;

      var logger = containerService.Kernel.LoggerFactory.CreateLogger(typeof(ServiceExtensions).FullName!);
      if (logger.IsEnabled(LogLevel.Debug))
      {
        logger.LogDebug(
            exception,
            "Container wait helper poll failed during {Operation} for {Value}",
            operation,
            value);
      }
    }

    #endregion

    #region Host Extensions

    /// <summary>
    /// Gets the Docker host address.
    /// </summary>
    /// <param name="service">The host service.</param>
    /// <returns>The Docker host address.</returns>
    /// <remarks>
    /// Uses <see cref="IServiceAsync.Name"/> when it contains a URI such as
    /// <c>tcp://host:2376</c>; otherwise falls back to localhost.
    /// </remarks>
    public static string GetDockerHost(this IHostService service)
    {
      if (service.IsNative)
        return "127.0.0.1";

      var configured = TryGetDockerHost(service.Name);
      if (!string.IsNullOrEmpty(configured))
        return configured;

      return "127.0.0.1";
    }

    private static string? TryGetDockerHost(string? value)
    {
      if (string.IsNullOrWhiteSpace(value) || value == "native")
        return null;

      if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host))
        return uri.Host;

      return value.Contains("://", StringComparison.Ordinal) ? null : value;
    }

    #endregion
  }
}
