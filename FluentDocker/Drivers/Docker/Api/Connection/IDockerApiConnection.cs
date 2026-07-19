#nullable disable warnings
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace FluentDocker.Drivers.Docker.Api.Connection
{
  /// <summary>
  /// Abstraction over HTTP communication with the Docker Engine REST API.
  /// Supports Unix domain sockets, named pipes, and TCP connections.
  /// </summary>
  public interface IDockerApiConnection : IAsyncDisposable
  {
    /// <summary>
    /// Sends a GET request to the Docker API.
    /// </summary>
    Task<HttpResponseMessage> GetAsync(string path, CancellationToken ct = default);

    /// <summary>
    /// Sends a HEAD request to the Docker API.
    /// </summary>
    Task<HttpResponseMessage> HeadAsync(string path, CancellationToken ct = default) =>
        throw new NotSupportedException("HEAD requests are not supported by this connection.");

    /// <summary>
    /// Sends a POST request to the Docker API.
    /// </summary>
    Task<HttpResponseMessage> PostAsync(string path, HttpContent content = null, CancellationToken ct = default);

    /// <summary>
    /// Sends a POST request with HTTP headers to the Docker API. Implementations that carry
    /// request headers (e.g. <c>X-Registry-Auth</c>) must override this; the default throws
    /// rather than silently dropping the headers.
    /// </summary>
    Task<HttpResponseMessage> PostAsync(
        string path, HttpContent content,
        IReadOnlyDictionary<string, string> headers, CancellationToken ct = default) =>
        throw new NotSupportedException(
            "POST with headers requires a connection that overrides this method.");

    /// <summary>
    /// Sends a PUT request to the Docker API.
    /// </summary>
    Task<HttpResponseMessage> PutAsync(string path, HttpContent content, CancellationToken ct = default);

    /// <summary>
    /// Sends a DELETE request to the Docker API.
    /// </summary>
    Task<HttpResponseMessage> DeleteAsync(string path, CancellationToken ct = default);

    /// <summary>
    /// Sends a GET request and returns the response body as a stream.
    /// Uses ResponseHeadersRead for efficient streaming of large responses.
    /// </summary>
    /// <remarks>
    /// Header arrival (time-to-first-byte) is bounded by the connection timeout; the returned
    /// body stream is not request-timeout bounded because Docker logs/build streams can be long-lived.
    /// </remarks>
    Task<Stream> GetStreamAsync(string path, CancellationToken ct = default);

    /// <summary>
    /// Sends a POST request and returns the response body as a stream.
    /// Used for streaming build output, pull progress, etc.
    /// </summary>
    /// <remarks>
    /// Header arrival (time-to-first-byte) is bounded by the connection timeout; the returned
    /// body stream is not request-timeout bounded because Docker logs/build streams can be long-lived.
    /// </remarks>
    Task<Stream> PostStreamAsync(string path, HttpContent content = null, CancellationToken ct = default);

    /// <summary>
    /// Sends a POST request with optional HTTP headers and returns the response body as a stream.
    /// </summary>
    /// <param name="path">Docker API path, without the negotiated version prefix.</param>
    /// <param name="content">Optional request content.</param>
    /// <param name="headers">Optional headers to add to the request without validation.</param>
    /// <param name="ct">Cancellation token for the request and response stream open.</param>
    /// <returns>The response body stream. Disposing the stream releases the HTTP response.</returns>
    Task<Stream> PostStreamAsync(
        string path, HttpContent content,
        IReadOnlyDictionary<string, string> headers, CancellationToken ct = default);

    /// <summary>
    /// Pings the Docker daemon to check connectivity.
    /// </summary>
    Task<bool> PingAsync(CancellationToken ct = default);

    /// <summary>
    /// The negotiated Docker Engine API version (e.g., "1.45").
    /// </summary>
    string ApiVersion { get; }
  }
}
