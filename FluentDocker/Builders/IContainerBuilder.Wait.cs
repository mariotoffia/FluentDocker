using System;
using System.Net.Http;
using FluentDocker.Common;
using FluentDocker.Services;

namespace FluentDocker.Builders
{
  public partial interface IContainerBuilder
  {
    #region Wait Conditions

    /// <summary>
    /// Waits for a container port to accept connections after starting.
    /// </summary>
    /// <param name="portAndProto">The port and protocol (e.g. "5432/tcp", "53/udp").</param>
    /// <param name="timeoutMs">Maximum time to wait in milliseconds. Defaults to 30000 (30 seconds).</param>
    /// <returns>The builder instance for method chaining.</returns>
    /// <remarks>
    /// Builder waits throw <see cref="FluentDockerException"/> on timeout; service
    /// extension waits return false.
    /// </remarks>
    IContainerBuilder WaitForPort(string portAndProto, long timeoutMs = 30000);

    /// <summary>
    /// Waits for a container port to accept connections at a specific address after starting.
    /// </summary>
    /// <param name="portAndProto">The port and protocol (e.g. "5432/tcp", "53/udp").</param>
    /// <param name="address">The IP address or hostname to connect to when probing the port.</param>
    /// <param name="timeoutMs">Maximum time to wait in milliseconds. Defaults to 30000 (30 seconds).</param>
    /// <returns>The builder instance for method chaining.</returns>
    IContainerBuilder WaitForPort(string portAndProto, string address, long timeoutMs = 30000);

    /// <summary>
    /// Waits for a named process to be running inside the container after starting.
    /// </summary>
    /// <param name="processName">The process name to look for (e.g. "postgres", "nginx").</param>
    /// <param name="timeoutMs">Maximum time to wait in milliseconds. Defaults to 30000 (30 seconds).</param>
    /// <returns>The builder instance for method chaining.</returns>
    /// <remarks>Uses <c>pgrep -f</c> inside the container; minimal images may not include it.</remarks>
    IContainerBuilder WaitForProcess(string processName, long timeoutMs = 30000);

    /// <summary>
    /// Waits for an HTTP endpoint inside the container to return a successful response.
    /// </summary>
    /// <param name="portAndProto">The port and protocol (e.g. "8080/tcp").</param>
    /// <param name="path">The HTTP path to request. Defaults to "/".</param>
    /// <param name="timeoutMs">Maximum time to wait in milliseconds. Defaults to 30000 (30 seconds).</param>
    /// <returns>The builder instance for method chaining.</returns>
    IContainerBuilder WaitForHttp(string portAndProto, string path = "/", long timeoutMs = 30000);

    /// <summary>
    /// Waits for an HTTP endpoint inside the container to return a successful response.
    /// </summary>
    /// <param name="portAndProto">The port and protocol (e.g. "8080/tcp").</param>
    /// <param name="timeoutMs">Maximum time to wait in milliseconds. Defaults to 30000 (30 seconds).</param>
    /// <returns>The builder instance for method chaining.</returns>
    IContainerBuilder WaitForHttp(string portAndProto, long timeoutMs);

    /// <summary>
    /// Waits for an HTTP endpoint with advanced options such as custom method, body, and continuation logic.
    /// </summary>
    /// <param name="url">The full URL to probe.</param>
    /// <param name="timeoutMs">Maximum time to wait in milliseconds. Defaults to 30000 (30 seconds).</param>
    /// <param name="method">The HTTP method to use. Defaults to GET when null.</param>
    /// <param name="contentType">The Content-Type header value for the request body.</param>
    /// <param name="body">The request body content.</param>
    /// <param name="continuation">
    /// A callback invoked after each HTTP response. Receives the <see cref="RequestResponse"/> and the
    /// current attempt count. Return a positive value in milliseconds to retry after that delay,
    /// 0 to continue immediately, or -1 to indicate success.
    /// </param>
    /// <returns>The builder instance for method chaining.</returns>
    IContainerBuilder WaitForHttpUrl(
        string url,
        long timeoutMs = 30000,
        HttpMethod method = null,
        string contentType = null,
        string body = null,
        Func<RequestResponse, int, long> continuation = null);

    /// <summary>
    /// Waits for a specific message to appear in the container's log output after starting.
    /// </summary>
    /// <param name="message">The log message substring to wait for.</param>
    /// <param name="timeoutMs">Maximum time to wait in milliseconds. Defaults to 30000 (30 seconds).</param>
    /// <returns>The builder instance for method chaining.</returns>
    IContainerBuilder WaitForLogMessage(string message, long timeoutMs = 30000);

    /// <summary>
    /// Waits for the container's health check to report "healthy" (requires a HEALTHCHECK in the image).
    /// </summary>
    /// <param name="timeoutMs">Maximum time to wait in milliseconds. Defaults to 30000 (30 seconds).</param>
    /// <returns>The builder instance for method chaining.</returns>
    IContainerBuilder WaitForHealthy(long timeoutMs = 30000);

    /// <summary>Registers a custom wait condition evaluated in a polling loop.</summary>
    /// <param name="condition">
    /// A function receiving the <see cref="IContainerService"/> and the current attempt count (zero-based).
    /// Return a positive value in milliseconds to retry after that delay,
    /// 0 to poll again after the default poll interval, or -1 to indicate success.
    /// </param>
    /// <param name="timeoutMs">Maximum time to wait in milliseconds. Defaults to 60000 (60 seconds).</param>
    /// <returns>The builder instance for method chaining.</returns>
    IContainerBuilder Wait(Func<IContainerService, int, int> condition, long timeoutMs = 60000);

    /// <summary>
    /// Sets the poll interval for subsequent wait conditions.
    /// </summary>
    /// <param name="intervalMs">Delay in milliseconds between poll iterations (default 500).</param>
    /// <returns>The builder instance for method chaining.</returns>
    IContainerBuilder WithWaitPollInterval(int intervalMs);

    #endregion
  }
}
