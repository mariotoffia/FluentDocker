using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace FluentDocker.Drivers.Models.Connection
{
  /// <summary>
  /// A thin HTTP abstraction for a model runner's OpenAI-compatible endpoint.
  /// Parallel to the Docker API connection, but NOT Docker-versioned and NOT
  /// wrapped in the Docker response envelope — DMR speaks raw OpenAI JSON.
  /// </summary>
  public interface IModelApiConnection : IAsyncDisposable
  {
    /// <summary>The base address the connection targets.</summary>
    Uri BaseAddress { get; }

    /// <summary>
    /// Maximum time to wait for streaming response headers or the first body bytes before aborting
    /// with <see cref="FluentDocker.Common.ModelRunnerException"/>
    /// (<see cref="FluentDocker.Model.Drivers.ErrorCodes.ModelInference.Timeout"/>).
    /// <c>null</c> disables the first-byte timeout.
    /// </summary>
    TimeSpan? StreamFirstByteTimeout { get; }

    /// <summary>
    /// Maximum time to wait between successive chunks of a streaming (SSE) read
    /// before aborting with <see cref="FluentDocker.Common.ModelRunnerException"/>
    /// (<see cref="FluentDocker.Model.Drivers.ErrorCodes.ModelInference.Timeout"/>).
    /// <c>null</c> disables the idle timeout — reads wait indefinitely, honoring
    /// only the caller's <see cref="System.Threading.CancellationToken"/>.
    /// </summary>
    TimeSpan? StreamReadIdleTimeout { get; }

    /// <summary>Issues a GET request.</summary>
    /// <param name="path">The request path.</param>
    /// <param name="ct">A token to cancel the request.</param>
    /// <returns>The HTTP response.</returns>
    Task<HttpResponseMessage> GetAsync(string path, CancellationToken ct = default);

    /// <summary>Issues a POST request.</summary>
    /// <param name="path">The request path.</param>
    /// <param name="content">The request body.</param>
    /// <param name="ct">A token to cancel the request.</param>
    /// <returns>The HTTP response.</returns>
    Task<HttpResponseMessage> PostAsync(string path, HttpContent content, CancellationToken ct = default);

    /// <summary>Issues a DELETE request.</summary>
    /// <param name="path">The request path.</param>
    /// <param name="ct">A token to cancel the request.</param>
    /// <returns>The HTTP response.</returns>
    Task<HttpResponseMessage> DeleteAsync(string path, CancellationToken ct = default);

    /// <summary>Issues a POST request and returns the response body stream (for SSE).</summary>
    /// <param name="path">The request path.</param>
    /// <param name="content">The request body.</param>
    /// <param name="ct">A token to cancel the request.</param>
    /// <returns>The response body stream (the caller disposes it).</returns>
    Task<Stream> PostStreamAsync(string path, HttpContent content, CancellationToken ct = default);

    /// <summary>
    /// Probes pure endpoint reachability. Any HTTP response counts as reachable; status-specific
    /// health checks (for example 404/405 from the model-list path) belong in status probes.
    /// </summary>
    /// <param name="ct">A token to cancel the request.</param>
    /// <returns><c>true</c> when the endpoint responds.</returns>
    Task<bool> PingAsync(CancellationToken ct = default);
  }
}
