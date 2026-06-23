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
    public ModelApiConnection(Uri baseAddress, HttpMessageHandler handler, ILoggerFactory loggerFactory = null)
    {
      ArgumentNullException.ThrowIfNull(baseAddress);
      ArgumentNullException.ThrowIfNull(handler);
      _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<ModelApiConnection>();
      _httpClient = new HttpClient(handler, disposeHandler: true)
      {
        BaseAddress = baseAddress,
        Timeout = Timeout.InfiniteTimeSpan
      };
    }

    /// <inheritdoc />
    public Uri BaseAddress => _httpClient.BaseAddress;

    /// <inheritdoc />
    public Task<HttpResponseMessage> GetAsync(string path, CancellationToken ct = default) =>
        _httpClient.GetAsync(path, ct);

    /// <inheritdoc />
    public Task<HttpResponseMessage> PostAsync(string path, HttpContent content, CancellationToken ct = default) =>
        _httpClient.PostAsync(path, content, ct);

    /// <inheritdoc />
    public Task<HttpResponseMessage> DeleteAsync(string path, CancellationToken ct = default) =>
        _httpClient.DeleteAsync(path, ct);

    /// <inheritdoc />
    public async Task<Stream> PostStreamAsync(string path, HttpContent content, CancellationToken ct = default)
    {
      var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = content };
      var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
      response.EnsureSuccessStatusCode();
      var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
      return new ResponseOwningStream(stream, response);
    }

    /// <inheritdoc />
    public async Task<bool> PingAsync(CancellationToken ct = default)
    {
      try
      {
        using var response = await _httpClient.GetAsync("/", ct).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
      }
      catch (Exception ex)
      {
        _logger.LogDebug(ex, "Model API ping failed");
        return false;
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
          var endpoint = new UnixDomainSocketEndPoint(socketPath);
          await socket.ConnectAsync(endpoint, ct).ConfigureAwait(false);
          return new NetworkStream(socket, ownsSocket: true);
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
      return (handler, new Uri($"{scheme}://{uri.Host}:{port}"));
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
            sslOptions.RemoteCertificateValidationCallback = (_, cert, chain, errors) =>
            {
              if (errors == SslPolicyErrors.None)
                return true;
              if (chain == null || cert == null)
                return false;
              chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
              chain.ChainPolicy.CustomTrustStore.Add(caCert);
              return chain.Build(new X509Certificate2(cert));
            };
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
