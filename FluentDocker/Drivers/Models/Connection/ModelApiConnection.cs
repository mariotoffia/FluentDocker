using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Model.Drivers;
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
  public sealed partial class ModelApiConnection : IModelApiConnection
  {
    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;
    // X509Certificate2 instances we created (client cert(s) + custom CA) — they own
    // native handles and must be disposed when the connection is. They are kept alive
    // for the lifetime of the connection because the CA cert is captured by the TLS
    // validation callback and the client certs are referenced by the handler; they are
    // disposed only AFTER _httpClient.Dispose() in DisposeAsync. Empty when there is no TLS.
    private readonly IReadOnlyList<X509Certificate2> _ownedCertificates;
    // Applied to non-streaming requests via a linked CTS so they cannot hang forever;
    // streaming (PostStreamAsync) is intentionally exempt and relies on the caller's
    // token, since inference/SSE can legitimately run for a long time.
    private readonly TimeSpan _requestTimeout;
    private readonly TimeSpan? _streamReadIdleTimeout; // null = no idle timeout on streaming reads
    // The path PingAsync probes for reachability — the OpenAI model-list route on the
    // endpoint's RESOLVED base path (e.g. /engines/llama.cpp/v1/models, or whatever a
    // Raw(...) base resolves to). Probing "/" can false-negative for endpoints whose only
    // served surface is under /engines/.../v1 (or an OpenAI server exposing only /v1/*).
    private readonly string _pingPath;

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

      var ownedCertificates = new List<X509Certificate2>();
      var (handler, baseAddress) = CreateHandler(endpoint, config, ownedCertificates);
      _ownedCertificates = ownedCertificates;
      _httpClient = new HttpClient(handler, disposeHandler: true)
      {
        BaseAddress = baseAddress,
        // Inference and SSE streams can legitimately run for a long time; rely on
        // the caller's CancellationToken for cancellation rather than a whole-
        // operation HttpClient timeout (which would abort long generations/streams).
        Timeout = Timeout.InfiniteTimeSpan
      };
      _requestTimeout = Normalize(config.RequestTimeout);
      _streamReadIdleTimeout = config.StreamReadIdleTimeout;
      // Probe the OpenAI model-list route on the endpoint's resolved base path rather than
      // "/" so a runner that only serves /engines/.../v1/* is still reported reachable.
      _pingPath = endpoint.EngineV1Path("/models");

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
      // This overload wraps a caller-supplied handler and creates no certificates of its own.
      _ownedCertificates = Array.Empty<X509Certificate2>();
      _httpClient = new HttpClient(handler, disposeHandler: true)
      {
        BaseAddress = baseAddress,
        Timeout = Timeout.InfiniteTimeSpan
      };
      _requestTimeout = Normalize(requestTimeout);
      // No endpoint is supplied via this overload, so derive the model-list probe path from
      // the base address's own path (e.g. http://host/engines/v1 -> /engines/v1/models).
      _pingPath = baseAddress.AbsolutePath.TrimEnd('/') + "/models";
    }

    /// <summary>Treats non-positive timeouts (incl. <c>default</c>) as infinite.</summary>
    private static TimeSpan Normalize(TimeSpan timeout) =>
        timeout > TimeSpan.Zero ? timeout : Timeout.InfiniteTimeSpan;

    /// <inheritdoc />
    public Uri BaseAddress => _httpClient.BaseAddress;

    /// <inheritdoc />
    public TimeSpan? StreamReadIdleTimeout => _streamReadIdleTimeout;

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
      HttpResponseMessage response;
      try
      {
        response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
      }
      catch (Exception ex) when (IsTransportFailure(ex))
      {
        // A connection-refused / DNS / socket failure opening the stream is "unreachable".
        // (An HTTP error STATUS is delivered as a response below, not thrown here.)
        throw EndpointUnreachable(ex);
      }
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
    /// Hard cap on how many bytes of a non-success response body are read into memory
    /// before building an exception message. A hostile or misbehaving server could send an
    /// arbitrarily large error body; bounding the READ (not just the final string) keeps
    /// error handling allocation-safe.
    /// </summary>
    private const int MaxErrorBodyBytes = 64 * 1024;

    /// <summary>
    /// Maximum number of characters from the body that are kept in the exception message.
    /// Anything past this is truncated and replaced with <see cref="ErrorBodyTruncationMarker"/>.
    /// </summary>
    private const int MaxErrorBodyChars = 512;

    /// <summary>Appended to a truncated error body so it is visibly incomplete.</summary>
    private const string ErrorBodyTruncationMarker = "…";

    /// <summary>
    /// Reads a non-success response body, bounded both in bytes read (<see cref="MaxErrorBodyBytes"/>)
    /// and in characters retained (<see cref="MaxErrorBodyChars"/>), for use in an exception
    /// message. Caller cancellation propagates; any other read failure is swallowed (it must not
    /// mask the underlying HTTP failure).
    /// </summary>
    private static async Task<string> ReadBoundedErrorBodyAsync(HttpResponseMessage response, CancellationToken ct)
    {
      try
      {
        var body = await ReadBoundedBodyTextAsync(response, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(body))
          return null;

        if (body.Length <= MaxErrorBodyChars)
          return body;

        // Keep the marker WITHIN the cap so the final message length never exceeds it.
        var keep = MaxErrorBodyChars - ErrorBodyTruncationMarker.Length;
        return body[..keep] + ErrorBodyTruncationMarker;
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

    /// <summary>
    /// Reads at most <see cref="MaxErrorBodyBytes"/> bytes of the response body and decodes
    /// them as UTF-8, so a pathological error body cannot force unbounded buffering.
    /// </summary>
    private static async Task<string> ReadBoundedBodyTextAsync(HttpResponseMessage response, CancellationToken ct)
    {
      await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
      var buffer = new byte[MaxErrorBodyBytes];
      var total = 0;
      while (total < buffer.Length)
      {
        var read = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), ct).ConfigureAwait(false);
        if (read == 0)
          break;
        total += read;
      }

      return total == 0 ? null : System.Text.Encoding.UTF8.GetString(buffer, 0, total);
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
        // Probe the OpenAI model-list route (not "/") so an endpoint that only serves
        // /engines/.../v1/* is not false-negatived. ANY HTTP response — including 4xx/5xx —
        // proves the endpoint is reachable; only a transport-level failure (connection
        // refused / DNS / socket / timeout) means unreachable.
        using var response = await _httpClient.GetAsync(_pingPath, token).ConfigureAwait(false);
        return true;
      }
      catch (OperationCanceledException) when (ct.IsCancellationRequested)
      {
        // Caller-requested cancellation is not a "ping failed" signal — propagate it.
        throw;
      }
      catch (Exception ex)
      {
        // Transport failure or a ping-timeout (the linked token fired but the caller's did
        // not) — the endpoint is unreachable.
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
      {
        try
        {
          return await send(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsTransportFailure(ex))
        {
          throw EndpointUnreachable(ex);
        }
      }

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
      catch (Exception ex) when (IsTransportFailure(ex))
      {
        // A connection-refused / DNS / socket failure on a non-streaming send is "the
        // endpoint is unreachable", not a generic request failure. The HttpClient verb
        // methods (Get/Post/Delete) never throw on an HTTP error STATUS — they return the
        // response — so any HttpRequestException/SocketException reaching here is transport.
        throw EndpointUnreachable(ex);
      }
    }

    /// <summary>
    /// True when <paramref name="ex"/> is a transport-level connection failure (connection
    /// refused / DNS / socket) rather than an HTTP error response. An
    /// <see cref="HttpRequestException"/> that carries an HTTP <see cref="HttpRequestException.StatusCode"/>
    /// is a real response (handled by the caller), so it is NOT treated as a transport failure.
    /// </summary>
    private static bool IsTransportFailure(Exception ex) => ex switch
    {
      HttpRequestException { StatusCode: not null } => false,
      HttpRequestException => true,
      SocketException => true,
      _ => false
    };

    private static ModelRunnerException EndpointUnreachable(Exception inner) =>
        new($"The model runner endpoint is unreachable ({inner.Message}). " +
            $"The default is host TCP http://localhost:12434 — ensure `docker model` is running, " +
            $"or set DOCKER_MODEL_RUNNER_URL, " +
            $"or pass an explicit endpoint (unix socket / container-internal). " +
            $"See docs/model-runner.md.",
            ErrorCodes.ModelInference.EndpointUnreachable, inner);

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
      _httpClient.Dispose();

      // Dispose any X509Certificate2 we created (client cert(s) + custom CA) to release
      // their native handles. This runs AFTER _httpClient.Dispose() so the handler is no
      // longer using the client certificates, and the CA cert captured by the TLS
      // validation callback is no longer reachable. Disposing an X509Certificate2 twice
      // is a no-op, so this is safe to call again (idempotent dispose).
      foreach (var certificate in _ownedCertificates)
        certificate.Dispose();

      GC.SuppressFinalize(this);
      return ValueTask.CompletedTask;
    }

    // ownedCertificates collects every X509Certificate2 created here so the connection
    // instance can dispose them; the unix-socket path adds none.
    private static (SocketsHttpHandler handler, Uri baseAddress) CreateHandler(
        ModelRunnerEndpoint endpoint, ModelApiConnectionConfig config, List<X509Certificate2> ownedCertificates)
    {
      if (!string.IsNullOrEmpty(endpoint.UnixSocketPath))
        return CreateUnixSocketHandler(endpoint.UnixSocketPath, config);

      return CreateTcpHandler(endpoint.BaseAddress, config, ownedCertificates);
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

    private static (SocketsHttpHandler, Uri) CreateTcpHandler(
        Uri uri, ModelApiConnectionConfig config, List<X509Certificate2> ownedCertificates)
    {
      var handler = new SocketsHttpHandler { ConnectTimeout = config.ConnectionTimeout };
      var hasCerts = !string.IsNullOrEmpty(config.CertificatePath);
      var useTls = string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase);

      if (useTls || hasCerts)
        handler.SslOptions = BuildSslOptions(config, ownedCertificates);

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

    // Every X509Certificate2 created here is added to ownedCertificates so the connection
    // instance can dispose them (they own native handles); they outlive this method
    // because the client certs are referenced by the handler and the CA cert is captured
    // by the validation callback below.
    private static SslClientAuthenticationOptions BuildSslOptions(
        ModelApiConnectionConfig config, List<X509Certificate2> ownedCertificates)
    {
      var sslOptions = new SslClientAuthenticationOptions();
      var hasCerts = !string.IsNullOrEmpty(config.CertificatePath);

      if (hasCerts)
      {
        var certPath = Path.Combine(config.CertificatePath, "cert.pem");
        var keyPath = Path.Combine(config.CertificatePath, "key.pem");
        if (File.Exists(certPath) && File.Exists(keyPath))
        {
          var clientCert = LoadClientCertificate(certPath, keyPath);
          ownedCertificates.Add(clientCert);
          sslOptions.ClientCertificates = [clientCert];
        }

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
            ownedCertificates.Add(caCert);
            // Trust the custom CA for chain validation only — hostname mismatch and a
            // missing certificate are still rejected by default (see ModelTlsValidation).
            sslOptions.RemoteCertificateValidationCallback = (_, cert, chain, errors) =>
                ModelTlsValidation.ValidateWithCustomRoot(caCert, cert, chain, errors, config.AllowTlsHostnameMismatch);
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
