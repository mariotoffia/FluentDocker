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
    // Applied to non-streaming requests via a linked CTS so they cannot hang forever.
    // Streaming is exempt from this whole-request timeout; its open/first-body wait and
    // subsequent reads use stream-specific budgets.
    private readonly TimeSpan _requestTimeout;
    private readonly TimeSpan? _streamFirstByteTimeout; // null = no first-byte timeout on streaming opens/reads
    private readonly TimeSpan? _streamReadIdleTimeout; // null = no idle timeout on streaming reads
    // The path PingAsync probes for reachability — the OpenAI model-list route on the
    // endpoint's RESOLVED base path (e.g. /engines/llama.cpp/v1/models, or whatever a
    // Raw(...) base resolves to). Probing "/" can false-negative for endpoints whose only
    // served surface is under /engines/.../v1 (or an OpenAI server exposing only /v1/*).
    private readonly string _pingPath;
    private int _disposed;

    /// <summary>
    /// Creates a connection from a resolved endpoint (TCP or unix socket).
    /// </summary>
    /// <param name="endpoint">The resolved runner endpoint.</param>
    /// <param name="config">Optional transport configuration.</param>
    /// <param name="loggerFactory">Optional logger factory.</param>
    /// <param name="apiKey">Optional bearer token (sent as <c>Authorization: Bearer …</c>, never logged).</param>
    public ModelApiConnection(ModelRunnerEndpoint endpoint, ModelApiConnectionConfig config = null,
        ILoggerFactory? loggerFactory = null, string apiKey = null)
    {
      ArgumentNullException.ThrowIfNull(endpoint);
      config ??= new ModelApiConnectionConfig();
      _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<ModelApiConnection>();
      ValidateApiKeyTransport(endpoint, config, apiKey);

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
      _streamFirstByteTimeout = config.StreamFirstByteTimeout;
      _streamReadIdleTimeout = config.StreamReadIdleTimeout;
      // Probe the OpenAI model-list route on the endpoint's resolved base path rather than
      // "/" so a runner that only serves /engines/.../v1/* is still reported reachable.
      _pingPath = endpoint.EngineV1Path("/models");

      if (!string.IsNullOrEmpty(apiKey))
      {
        _httpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
      }
    }

    /// <summary>
    /// Creates a connection wrapping a caller-supplied message handler (for testing
    /// or custom transports).
    /// </summary>
    /// <param name="baseAddress">The base address.</param>
    /// <param name="handler">The message handler.</param>
    /// <param name="loggerFactory">Optional logger factory.</param>
    /// <param name="requestTimeout">Per-request timeout for non-streaming calls (default = infinite).</param>
    /// <remarks>
    /// Legacy overload: streaming idle/header timeout is left unset for binary compatibility.
    /// Prefer the <see cref="ModelApiConnection(Uri, HttpMessageHandler, ILoggerFactory, ModelApiConnectionConfig)"/>
    /// overload for custom transports that should inherit the documented streaming protections.
    /// </remarks>
    public ModelApiConnection(Uri baseAddress, HttpMessageHandler handler, ILoggerFactory? loggerFactory = null,
        TimeSpan requestTimeout = default)
        : this(baseAddress, handler, loggerFactory, Normalize(requestTimeout), null, null)
    {
    }

    /// <summary>
    /// Creates a connection wrapping a caller-supplied message handler and applying the
    /// same transport configuration as endpoint-created connections.
    /// </summary>
    /// <param name="baseAddress">The base address.</param>
    /// <param name="handler">The message handler.</param>
    /// <param name="loggerFactory">Optional logger factory.</param>
    /// <param name="config">Transport configuration.</param>
    public ModelApiConnection(Uri baseAddress, HttpMessageHandler handler, ILoggerFactory? loggerFactory,
        ModelApiConnectionConfig config)
        : this(baseAddress, handler, loggerFactory, Normalize(RequireConfig(config).RequestTimeout),
            RequireConfig(config).StreamFirstByteTimeout,
            RequireConfig(config).StreamReadIdleTimeout)
    {
    }

    private ModelApiConnection(Uri baseAddress, HttpMessageHandler handler, ILoggerFactory? loggerFactory,
        TimeSpan requestTimeout, TimeSpan? streamFirstByteTimeout, TimeSpan? streamReadIdleTimeout)
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
      _requestTimeout = requestTimeout;
      _streamFirstByteTimeout = streamFirstByteTimeout;
      _streamReadIdleTimeout = streamReadIdleTimeout;
      // No endpoint is supplied via this overload, so derive the model-list probe path from
      // the base address's own path (e.g. http://host/engines/v1 -> /engines/v1/models).
      _pingPath = baseAddress.AbsolutePath.TrimEnd('/') + "/models";
    }

    private static ModelApiConnectionConfig RequireConfig(ModelApiConnectionConfig config) =>
        config ?? throw new ArgumentNullException(nameof(config));

    /// <summary>Treats non-positive timeouts (incl. <c>default</c>) as infinite.</summary>
    private static TimeSpan Normalize(TimeSpan timeout) =>
        timeout > TimeSpan.Zero ? timeout : Timeout.InfiniteTimeSpan;

    /// <inheritdoc />
    public Uri BaseAddress => _httpClient.BaseAddress;

    /// <inheritdoc />
    public TimeSpan? StreamFirstByteTimeout => _streamFirstByteTimeout;

    /// <inheritdoc />
    public TimeSpan? StreamReadIdleTimeout => _streamReadIdleTimeout;

    /// <inheritdoc />
    public Task<HttpResponseMessage> GetAsync(string path, CancellationToken ct = default) =>
        SendWithTimeoutAsync(c => SendAsync(HttpMethod.Get, path, null, c), ct);

    /// <inheritdoc />
    public Task<HttpResponseMessage> PostAsync(string path, HttpContent content, CancellationToken ct = default) =>
        SendWithTimeoutAsync(c => SendAsync(HttpMethod.Post, path, content, c), ct);

    /// <inheritdoc />
    public Task<HttpResponseMessage> DeleteAsync(string path, CancellationToken ct = default) =>
        SendWithTimeoutAsync(c => SendAsync(HttpMethod.Delete, path, null, c), ct);

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string path, HttpContent content, CancellationToken ct)
    {
      using var request = new HttpRequestMessage(method, path) { Content = content };
      return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
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
        using var response = await _httpClient.GetAsync(_pingPath, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
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
          return ApplyBodyTimeout(await send(ct).ConfigureAwait(false), DateTimeOffset.MaxValue);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested && IsTransportFailure(ex))
        {
          throw EndpointUnreachable(ex);
        }
      }

      // One wall-clock budget shared by the header phase (linked CTS) and the body-read phase
      // (deadline threaded into ApplyBodyTimeout) so a non-streaming request is bounded by a
      // single _requestTimeout, not one per phase.
      var deadline = DateTimeOffset.UtcNow.Add(_requestTimeout);
      using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
      linked.CancelAfter(_requestTimeout);
      try
      {
        return ApplyBodyTimeout(await send(linked.Token).ConfigureAwait(false), deadline);
      }
      catch (OperationCanceledException ex) when (!ct.IsCancellationRequested && IsTransportFailure(ex))
      {
        throw EndpointUnreachable(ex);
      }
      catch (OperationCanceledException) when (!ct.IsCancellationRequested)
      {
        throw new TimeoutException($"The model API request exceeded the configured request timeout of {_requestTimeout}.");
      }
      catch (Exception ex) when (!ct.IsCancellationRequested && IsTransportFailure(ex))
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
      OperationCanceledException { InnerException: TimeoutException } => true,
      OperationCanceledException { InnerException: OperationCanceledException inner } => IsTransportFailure(inner),
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
      if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        return ValueTask.CompletedTask;

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

    private static void ValidateApiKeyTransport(ModelRunnerEndpoint endpoint, ModelApiConnectionConfig config, string apiKey)
    {
      if (string.IsNullOrEmpty(apiKey))
        return;

      // Never leak the bearer token to a host that can be impersonated: plaintext http to
      // a non-loopback TCP host, or https with server-cert validation disabled (MITM
      // equivalent), is refused unless explicitly opted in. Validate before creating the
      // handler/certificates so this guard cannot leak native handles on throw.
      var baseAddress = endpoint.BaseAddress;
      var insecureTransport =
          string.Equals(baseAddress.Scheme, "http", StringComparison.OrdinalIgnoreCase)
          || !config.VerifyTls;
      if (insecureTransport
          && !baseAddress.IsLoopback
          && string.IsNullOrEmpty(endpoint.UnixSocketPath)
          && !config.AllowApiKeyOverInsecureTransport)
        throw new ModelRunnerException(
            $"Refusing to send the API key over an insecure transport (plaintext HTTP or unverified TLS) to non-loopback host '{baseAddress.Host}'. " +
            $"Use a verified https endpoint or a unix socket, or set {nameof(ModelApiConnectionConfig.AllowApiKeyOverInsecureTransport)}=true to acknowledge the insecure transport.",
            ErrorCodes.ModelInference.Unauthorized);
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
      if (!string.IsNullOrEmpty(config.CertificatePath))
        throw new ArgumentException(
            "ModelApiConnectionConfig.CertificatePath requires an https model runner endpoint; plaintext http cannot use client certificates.",
            nameof(config));

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
      var handler = new SocketsHttpHandler
      {
        ConnectTimeout = config.ConnectionTimeout,
        PooledConnectionLifetime = TimeSpan.FromMinutes(2)
      };
      var hasCerts = !string.IsNullOrEmpty(config.CertificatePath);
      var useTls = string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase);
      if (hasCerts && !useTls)
        throw new ArgumentException(
            "ModelApiConnectionConfig.CertificatePath requires an https model runner endpoint; plaintext http cannot use client certificates.",
            nameof(config));

      if (useTls || hasCerts)
        handler.SslOptions = BuildSslOptions(config, ownedCertificates);

      var scheme = useTls ? "https" : "http";
      // Honor the port exactly as written. The DMR default (:12434) is injected by the
      // ModelRunnerEndpoint factories (HostTcp/ContainerInternal/Default), not silently here —
      // so an explicit remote endpoint on a standard port (https://host => :443) is never
      // rewritten to :12434. Uri.Port is always the effective port for http/https.
      var port = uri.Port;
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
        var (certPath, keyPath, caPath, hasClientCertificate) = ValidateCertificatePath(config);
        if (hasClientCertificate)
        {
          var clientCert = ClientCertificateLoader.Load(certPath, keyPath);
          ownedCertificates.Add(clientCert);
          sslOptions.ClientCertificates = [clientCert];
        }
        if (!config.VerifyTls)
        {
#pragma warning disable CA5359 // Intentional: caller opted out via VerifyTls=false
          sslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;
#pragma warning restore CA5359
        }
        else if (caPath != null)
        {
#if NET9_0_OR_GREATER
          var caCert = X509CertificateLoader.LoadCertificateFromFile(caPath);
#else
          var caCert = X509Certificate2.CreateFromPem(File.ReadAllText(caPath));
#endif
          ownedCertificates.Add(caCert);
          // Pin: the custom CA is the exclusive trust root for chain validation; hostname
          // mismatch and a missing certificate are still rejected by default (see
          // ModelTlsValidation). Without ca.pem the system trust store applies (no callback).
          sslOptions.RemoteCertificateValidationCallback = (_, cert, chain, errors) =>
              ModelTlsValidation.ValidateWithCustomRoot(caCert, cert, chain, errors, config.AllowTlsHostnameMismatch);
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
