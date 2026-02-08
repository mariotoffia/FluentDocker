using System;
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
        /// Sends a POST request to the Docker API.
        /// </summary>
        Task<HttpResponseMessage> PostAsync(string path, HttpContent content = null, CancellationToken ct = default);

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
        Task<Stream> GetStreamAsync(string path, CancellationToken ct = default);

        /// <summary>
        /// Sends a POST request and returns the response body as a stream.
        /// Used for streaming build output, pull progress, etc.
        /// </summary>
        Task<Stream> PostStreamAsync(string path, HttpContent content = null, CancellationToken ct = default);

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
