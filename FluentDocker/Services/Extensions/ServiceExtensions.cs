using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Model.Containers;

namespace FluentDocker.Services.Extensions
{
  /// <summary>
  /// Extension methods for V3 service interfaces.
  /// </summary>
  public static class ServiceExtensions
  {
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
      var config = await service.InspectAsync(cancellationToken).ConfigureAwait(false);

      if (config?.NetworkSettings?.Ports == null)
        return null;

      if (!config.NetworkSettings.Ports.TryGetValue(portAndProto, out var bindings) ||
          bindings == null || bindings.Length == 0)
        return null;

      var binding = bindings.FirstOrDefault();
      if (binding == null)
        return null;

      var hostPort = int.Parse(binding.HostPort);
      var hostIp = binding.HostIp;

      // Resolve to localhost if HostIp is 0.0.0.0 or empty
      if (string.IsNullOrEmpty(hostIp) || hostIp == "0.0.0.0")
        hostIp = "127.0.0.1";

      return new IPEndPoint(IPAddress.Parse(hostIp), hostPort);
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
      var endpoint = await service.ToHostExposedEndpointAsync(portAndProto, cancellationToken)
          .ConfigureAwait(false);
      return endpoint?.Port ?? 0;
    }

    /// <summary>
    /// Waits for a port to become available asynchronously.
    /// </summary>
    /// <param name="service">The container service.</param>
    /// <param name="portAndProto">Port and protocol, e.g., "5432/tcp".</param>
    /// <param name="timeout">Timeout in milliseconds.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the port is available, false if timeout.</returns>
    public static async Task<bool> WaitForPortAsync(
        this IContainerService service,
        string portAndProto,
        long timeout = 30000,
        CancellationToken cancellationToken = default)
    {
      var endpoint = await service.ToHostExposedEndpointAsync(portAndProto, cancellationToken)
          .ConfigureAwait(false) ?? throw new FluentDockerException($"Port {portAndProto} is not exposed on container {service.Id}");

      return await WaitForPortAsync(endpoint.Address.ToString(), endpoint.Port, timeout, cancellationToken)
          .ConfigureAwait(false);
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
      var sw = Stopwatch.StartNew();

      while (sw.ElapsedMilliseconds < timeout && !cancellationToken.IsCancellationRequested)
      {
        try
        {
          using var client = new TcpClient();
          var connectTask = client.ConnectAsync(host, port);
          var remaining = (int)Math.Max(100, timeout - sw.ElapsedMilliseconds);
          var timeoutTask = Task.Delay(remaining, cancellationToken);

          var completed = await Task.WhenAny(connectTask, timeoutTask).ConfigureAwait(false);

          if (completed == connectTask && client.Connected)
            return true;

          // Observe faulted connectTask to prevent unobserved exceptions.
          _ = connectTask.ContinueWith(
              static t => { _ = t.Exception; },
              TaskContinuationOptions.OnlyOnFaulted);
        }
        catch (SocketException)
        {
          // Connection refused - port not ready yet
        }
        catch (OperationCanceledException)
        {
          break;
        }

        await Task.Delay(100, cancellationToken).ConfigureAwait(false);
      }

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
    public static async Task<bool> WaitForProcessAsync(
        this IContainerService service,
        string processName,
        long timeout = 30000,
        CancellationToken cancellationToken = default)
    {
      var sw = Stopwatch.StartNew();

      while (sw.ElapsedMilliseconds < timeout && !cancellationToken.IsCancellationRequested)
      {
        try
        {
          // Use docker top to check for process
          var result = await service.ExecuteAsync($"pgrep -f {processName}", cancellationToken)
              .ConfigureAwait(false);

          if (!string.IsNullOrWhiteSpace(result))
            return true;
        }
        catch (Exception ex)
        {
          Logger.Log($"Process wait check failed: {ex.Message}");
        }

        await Task.Delay(500, cancellationToken).ConfigureAwait(false);
      }

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
    /// <returns>True if the endpoint responds successfully, false if timeout.</returns>
    public static async Task<bool> WaitForHttpAsync(
        this IContainerService service,
        string portAndProto,
        string path = "/",
        long timeout = 30000,
        CancellationToken cancellationToken = default)
    {
      var endpoint = await service.ToHostExposedEndpointAsync(portAndProto, cancellationToken)
          .ConfigureAwait(false) ?? throw new FluentDockerException($"Port {portAndProto} is not exposed on container {service.Id}");

      var sw = Stopwatch.StartNew();
      var url = $"http://{endpoint.Address}:{endpoint.Port}{path}";

      while (sw.ElapsedMilliseconds < timeout && !cancellationToken.IsCancellationRequested)
      {
        try
        {
          using var requestCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
          var remainingMs = Math.Max(100, timeout - sw.ElapsedMilliseconds);
          requestCts.CancelAfter(TimeSpan.FromMilliseconds(remainingMs));

          var response = await Common.SharedHttpClient.Instance.GetAsync(url, requestCts.Token).ConfigureAwait(false);
          if (response.IsSuccessStatusCode)
            return true;
        }
        catch (HttpRequestException)
        {
          // Not ready yet
        }
        catch (TaskCanceledException)
        {
          // Timeout on request
        }

        await Task.Delay(500, cancellationToken).ConfigureAwait(false);
      }

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
    public static async Task<bool> WaitForLogMessageAsync(
        this IContainerService service,
        string text,
        long timeout = 30000,
        CancellationToken cancellationToken = default)
    {
      var sw = Stopwatch.StartNew();

      while (sw.ElapsedMilliseconds < timeout && !cancellationToken.IsCancellationRequested)
      {
        try
        {
          var logs = await service.GetLogsAsync(false, cancellationToken).ConfigureAwait(false);
          if (logs?.Contains(text) == true)
            return true;
        }
        catch (Exception ex)
        {
          Logger.Log($"Log content wait check failed: {ex.Message}");
        }

        await Task.Delay(500, cancellationToken).ConfigureAwait(false);
      }

      return false;
    }

    #endregion

    #region Host Extensions

    /// <summary>
    /// Gets the Docker host address.
    /// </summary>
    /// <param name="service">The host service.</param>
    /// <returns>The Docker host address.</returns>
    public static string GetDockerHost(this IHostService service)
    {
      // For native Docker, use localhost
      if (service.IsNative)
        return "127.0.0.1";

      // For remote Docker hosts, the address would be in the configuration
      // This is a simplified implementation
      return "127.0.0.1";
    }

    #endregion

    #region Sync Wrappers (for backward compatibility patterns)

    /// <summary>
    /// Gets the container configuration synchronously.
    /// </summary>
    public static Container GetConfiguration(this IContainerService service, bool fresh = false)
    {
      return service.GetConfigurationAsync(fresh).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Gets the host-exposed endpoint synchronously.
    /// </summary>
    public static IPEndPoint ToHostExposedEndpoint(this IContainerService service, string portAndProto)
    {
      return service.ToHostExposedEndpointAsync(portAndProto).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Gets the host port synchronously.
    /// </summary>
    public static int GetHostPort(this IContainerService service, string portAndProto)
    {
      return service.GetHostPortAsync(portAndProto).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Waits for a port synchronously.
    /// </summary>
    public static bool WaitForPort(this IContainerService service, string portAndProto, long timeout = 30000)
    {
      return service.WaitForPortAsync(portAndProto, timeout).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Waits for a process synchronously.
    /// </summary>
    public static bool WaitForProcess(this IContainerService service, string processName, long timeout = 30000)
    {
      return service.WaitForProcessAsync(processName, timeout).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Waits for HTTP endpoint synchronously.
    /// </summary>
    public static bool WaitForHttp(this IContainerService service, string portAndProto, string path = "/", long timeout = 30000)
    {
      return service.WaitForHttpAsync(portAndProto, path, timeout).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Waits for log message synchronously.
    /// </summary>
    public static bool WaitForLogMessage(this IContainerService service, string text, long timeout = 30000)
    {
      return service.WaitForLogMessageAsync(text, timeout).GetAwaiter().GetResult();
    }

    #endregion
  }
}

