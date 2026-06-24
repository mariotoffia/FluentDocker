using System;
using System.IO;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Model.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FluentDocker.Drivers.Models.Connection
{
  /// <summary>
  /// Default <see cref="IModelApiConnection"/> over an <see cref="HttpClient"/>:
  /// TCP/HTTP(S) or a unix domain socket (via
  /// <see cref="SocketsHttpHandler.ConnectCallback"/>). Unlike the Docker API
  /// connection it is not version-negotiated and not response-wrapped — DMR speaks
  /// raw OpenAI JSON.
  /// </summary>
  public sealed class ModelApiConnection : IModelApiConnection
  {
    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;
    // Applied to non-streaming requests via a linked CTS so they cannot hang forever;
    // streaming (PostStreamAsync) is intentionally exempt and relies on the caller's
    // token, since inference/SSE can legitimately run for a long time.
    private readonly TimeSpan _requestTimeout;

    /// <summary>
    /// Creates a connection from a resolved endpoint (TCP or unix socket).
    /// </summary>
    /// <param name="endpoint">The resolved runner endpoint.</param>
    /// <param name="config">Optional transport configuration.</param>
    /// <param name="loggerFactory">Optional logger factory.</param>
    /// <param name="apiKey">Optional bearer token (sent as <c>Authorization: Bearer …</c>, never logged).</param>
    public ModelApiConnection(ModelRunnerEndpoint endpoint, ModelApiConnectionConfig config = null,
        ILoggerFactory loggerFactory = null, string apiKey = null)
    {
      ArgumentNullException.ThrowIfNull(endpoint);
      config ??= new ModelApiConnectionConfig();
      _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<ModelApiConnection>();

      var (handler, baseAddress) = CreateHandler(endpoint, config);
      _httpClient = new HttpClient(handler, disposeHandler: true)
      {
        BaseAddress = baseAddress,
        // Inference and SSE streams can legitimately run for a long time; rely on
        // the caller's CancellationToken for cancellation rather than a whole-
        // operation HttpClient timeout (which would abort long generations/streams).
        Timeout = Timeout.InfiniteTimeSpan
      };
      _requestTimeout = Normalize(config.RequestTimeout);

      if (!string.IsNullOrEmpty(apiKey))
        _httpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
    }

    /// <summary>
    /// Creates a connection wrapping a caller-supplied message handler (for testing
    /// or custom transports).
    /// </summary>
    /// <param name="baseAddress">The base address.</param>
    /// <param name="handler">The message handler.</param>
    /// <param name="loggerFactory">Optional logger factory.</param>
    /// <param name="requestTimeout">
    /// Per-request timeout for non-streaming calls (default = infinite). Streaming
    /// calls are always exempt.
    /// </param>
    public ModelApiConnection(Uri baseAddress, HttpMessageHandler handler, ILoggerFactory loggerFactory = null,
        TimeSpan requestTimeout = default)
    {
      ArgumentNullException.ThrowIfNull(baseAddress);
      ArgumentNullException.ThrowIfNull(handler);
      _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<ModelApiConnection>();
      _httpClient = new HttpClient(handler, disposeHandler: true)
      {
        BaseAddress = baseAddress,
        Timeout = Timeout.InfiniteTimeSpan
      };
      _requestTimeout = Normalize(requestTimeout);
    }

    /// <summary>Treats non-positive timeouts (incl. <c>default</c>) as infinite.</summary>
    private static TimeSpan Normalize(TimeSpan timeout) =>
        timeout > TimeSpan.Zero ? timeout : Timeout.InfiniteTimeSpan;

    /// <inheritdoc />
    public Uri BaseAddress => _httpClient.BaseAddress;

    /// <inheritdoc />
    public Task<HttpResponseMessage> GetAsync(string path, CancellationToken ct = default) =>
        SendWithTimeoutAsync(c => _httpClient.GetAsync(path, c), ct);

    /// <inheritdoc />
    public Task<HttpResponseMessage> PostAsync(string path, HttpContent content, CancellationToken ct = default) =>
        SendWithTimeoutAsync(c => _httpClient.PostAsync(path, content, c), ct);

    /// <inheritdoc />
    public Task<HttpResponseMessage> DeleteAsync(string path, CancellationToken ct = default) =>
        SendWithTimeoutAsync(c => _httpClient.DeleteAsync(path, c), ct);

    /// <inheritdoc />
    public async Task<Stream> PostStreamAsync(string path, HttpContent content, CancellationToken ct = default)
    {
      // Streaming is exempt from the request timeout (SSE can run for a long time) —
      // we use the caller's token directly.
      var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = content };
      var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
      if (!response.IsSuccessStatusCode)
      {
        // Surface the status code AND a bounded error body so the inference driver can
        // map it to a typed ModelRunnerException (404 -> ModelNotLoaded, 401 ->
        // Unauthorized), mirroring the non-streaming path. EnsureSuccessStatusCode would
        // discard the body. Dispose the failed response before throwing so it does not
        // leak — ownership has not yet been transferred to ResponseOwningStream.
        var status = response.StatusCode;
        string body;
        try
        {
          body = await ReadBoundedErrorBodyAsync(response, ct).ConfigureAwait(false);
        }
        finally
        {
          response.Dispose();
        }

        throw new HttpRequestException(
            string.IsNullOrWhiteSpace(body) ? $"HTTP {(int)status}" : body, null, status);
      }

      Stream stream;
      try
      {
        stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
      }
      catch
      {
        // Ownership has not yet transferred to ResponseOwningStream — dispose the
        // response so it (and its connection) do not leak on a read failure.
        response.Dispose();
        throw;
      }

      return new ResponseOwningStream(stream, response);
    }

    /// <summary>
    /// Reads a non-success response body, truncated to a sane bound for use in an
    /// exception message. Caller cancellation propagates; any other read failure is
    /// swallowed (it must not mask the underlying HTTP failure).
    /// </summary>
    private static async Task<string> ReadBoundedErrorBodyAsync(HttpResponseMessage response, CancellationToken ct)
    {
      const int maxBodyChars = 512;
      try
      {
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(body))
          return null;

        return body.Length <= maxBodyChars ? body : body[..maxBodyChars];
      }
      catch (OperationCanceledException) when (ct.IsCancellationRequested)
      {
        throw;
      }
      catch (Exception)
      {
        return null;
      }
    }

    /// <inheritdoc />
    public async Task<bool> PingAsync(CancellationToken ct = default)
    {
      // Bound the probe by the request timeout — HttpClient.Timeout is infinite, so an
      // endpoint that accepts the connection but never responds would otherwise hang the
      // ping forever. A ping that times out reports unreachable (false), not an exception.
      using var linked = _requestTimeout == Timeout.InfiniteTimeSpan
          ? null
          : CancellationTokenSource.CreateLinkedTokenSource(ct);
      linked?.CancelAfter(_requestTimeout);
      var token = linked?.Token ?? ct;

      try
      {
        using var response = await _httpClient.GetAsync("/", token).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
      }
      catch (OperationCanceledException) when (ct.IsCancellationRequested)
      {
        // Caller-requested cancellation is not a "ping failed" signal — propagate it.
        throw;
      }
      catch (Exception ex)
      {
        // Includes a ping-timeout (the linked token fired but the caller's did not).
        _logger.LogDebug(ex, "Model API ping failed");
        return false;
      }
    }

    /// <summary>
    /// Runs a non-streaming request under <see cref="_requestTimeout"/> (when finite)
    /// via a linked CTS, surfacing a timeout as <see cref="TimeoutException"/> while
    /// still honoring the caller's cancellation token.
    /// </summary>
    private async Task<HttpResponseMessage> SendWithTimeoutAsync(
        Func<CancellationToken, Task<HttpResponseMessage>> send, CancellationToken ct)
    {
      if (_requestTimeout == Timeout.InfiniteTimeSpan)
        return await send(ct).ConfigureAwait(false);

      using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
      linked.CancelAfter(_requestTimeout);
      try
      {
        return await send(linked.Token).ConfigureAwait(false);
      }
      catch (OperationCanceledException) when (!ct.IsCancellationRequested)
      {
        throw new TimeoutException($"The model API request exceeded the configured request timeout of {_requestTimeout}.");
      }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
      _httpClient.Dispose();
      GC.SuppressFinalize(this);
      return ValueTask.CompletedTask;
    }

    private static (SocketsHttpHandler handler, Uri baseAddress) CreateHandler(ModelRunnerEndpoint endpoint, ModelApiConnectionConfig config)
    {
      if (!string.IsNullOrEmpty(endpoint.UnixSocketPath))
        return CreateUnixSocketHandler(endpoint.UnixSocketPath, config);

      return CreateTcpHandler(endpoint.BaseAddress, config);
    }

    private static (SocketsHttpHandler, Uri) CreateUnixSocketHandler(string socketPath, ModelApiConnectionConfig config)
    {
      var handler = new SocketsHttpHandler
      {
        ConnectCallback = async (_, ct) =>
        {
          var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
          try
          {
            var endpoint = new UnixDomainSocketEndPoint(socketPath);
            await socket.ConnectAsync(endpoint, ct).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
          }
          catch
          {
            // NetworkStream never took ownership — dispose the socket so a failed
            // connect (bad path, timeout, cancellation) does not leak the descriptor.
            socket.Dispose();
            throw;
          }
        },
        ConnectTimeout = config.ConnectionTimeout
      };

      return (handler, new Uri("http://localhost"));
    }

    private static (SocketsHttpHandler, Uri) CreateTcpHandler(Uri uri, ModelApiConnectionConfig config)
    {
      var handler = new SocketsHttpHandler { ConnectTimeout = config.ConnectionTimeout };
      var hasCerts = !string.IsNullOrEmpty(config.CertificatePath);
      var useTls = string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase);

      if (useTls || hasCerts)
        handler.SslOptions = BuildSslOptions(config);

      var scheme = (useTls || hasCerts) ? "https" : "http";
      var port = uri.Port > 0 ? uri.Port : 12434;
      // Rebuild the authority via UriBuilder (not string interpolation) so an IPv6
      // literal host is bracketed correctly (e.g. http://[::1]:12434) and any path /
      // query / user-info on the source URI is dropped.
      var baseAddress = new UriBuilder
      {
        Scheme = scheme,
        Host = uri.Host,
        Port = port
      }.Uri;
      return (handler, baseAddress);
    }

    private static SslClientAuthenticationOptions BuildSslOptions(ModelApiConnectionConfig config)
    {
      var sslOptions = new SslClientAuthenticationOptions();
      var hasCerts = !string.IsNullOrEmpty(config.CertificatePath);

      if (hasCerts)
      {
        var certPath = Path.Combine(config.CertificatePath, "cert.pem");
        var keyPath = Path.Combine(config.CertificatePath, "key.pem");
        if (File.Exists(certPath) && File.Exists(keyPath))
          sslOptions.ClientCertificates = [X509Certificate2.CreateFromPemFile(certPath, keyPath)];

        if (!config.VerifyTls)
        {
#pragma warning disable CA5359 // Intentional: caller opted out via VerifyTls=false
          sslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;
#pragma warning restore CA5359
        }
        else
        {
          var caPath = Path.Combine(config.CertificatePath, "ca.pem");
          if (File.Exists(caPath))
          {
#if NET9_0_OR_GREATER
            var caCert = X509CertificateLoader.LoadCertificateFromFile(caPath);
#else
            var caCert = X509Certificate2.CreateFromPemFile(caPath);
#endif
            // Trust the custom CA for chain validation only — hostname mismatch and a
            // missing certificate are still rejected (see ModelTlsValidation).
            sslOptions.RemoteCertificateValidationCallback = (_, cert, chain, errors) =>
                ModelTlsValidation.ValidateWithCustomRoot(caCert, cert, chain, errors);
          }
        }
      }
      else if (!config.VerifyTls)
      {
#pragma warning disable CA5359
        sslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;
#pragma warning restore CA5359
      }

      return sslOptions;
    }
  }
}
