using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers.Models.Connection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ResponseOwningStream = FluentDocker.Drivers.Connection.ResponseOwningStream;

namespace FluentDocker.Drivers.Docker.Api.Connection
{
  /// <summary>
  /// HTTP connection to the Docker Engine REST API.
  /// Supports Unix domain sockets, Windows named pipes, and TCP with optional TLS.
  /// </summary>
  public sealed class DockerApiConnection : IDockerApiConnection
  {
    private readonly HttpClient _httpClient;
    private readonly HttpClient _longRunningHttpClient;
    private readonly DockerApiConnectionConfig _config;
    private readonly SemaphoreSlim _negotiationLock = new(1, 1);
    // X509Certificate2 instances we created (client cert + custom CA) — they own
    // native handles and must be disposed when the connection is. They are kept alive
    // for the lifetime of the connection because the CA cert is captured by the TLS
    // validation callback and the client cert is referenced by the handler; they are
    // disposed only AFTER _httpClient.Dispose() in DisposeAsync. Empty when there is no TLS.
    private readonly IReadOnlyList<X509Certificate2> _ownedCertificates;

    /// <summary>
    /// Immutable record holding the negotiation result. A single volatile reference
    /// ensures both the API version and the negotiated flag are published atomically,
    /// preventing other threads from observing a partially-written state.
    /// </summary>
    private sealed record NegotiationState(string ApiVersion, bool Negotiated);

    private volatile NegotiationState _negotiation;
    private readonly ILogger<DockerApiConnection> _logger;

    public DockerApiConnection(DockerApiConnectionConfig config, ILoggerFactory loggerFactory = null)
    {
      ArgumentNullException.ThrowIfNull(config);
      _config = config;
      _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<DockerApiConnection>();

      var host = config.Host ?? GetDefaultHost();
      var ownedCertificates = new List<X509Certificate2>();
      var (handler, baseAddress) = CreateHandler(host, config, ownedCertificates);
      _ownedCertificates = ownedCertificates;

      var baseUri = new Uri(baseAddress);
      _httpClient = new HttpClient(handler, disposeHandler: true)
      {
        BaseAddress = baseUri,
        Timeout = config.RequestTimeout
      };
      _longRunningHttpClient = new HttpClient(handler, disposeHandler: false)
      {
        BaseAddress = baseUri,
        Timeout = Timeout.InfiniteTimeSpan
      };

      // If the user pre-set ApiVersion, mark negotiation as already done.
      _negotiation = !string.IsNullOrEmpty(config.ApiVersion)
          ? new NegotiationState(config.ApiVersion, Negotiated: true)
          : new NegotiationState(null, Negotiated: false);
    }

    public string ApiVersion => _negotiation.ApiVersion;

    /// <summary>
    /// Whether API version negotiation has completed (either via config or ping).
    /// </summary>
    public bool IsVersionNegotiated => _negotiation.Negotiated;

    public async Task<HttpResponseMessage> GetAsync(string path, CancellationToken ct = default)
    {
      var versionedPath = await GetVersionedPathAsync(path, ct).ConfigureAwait(false);
      return await _httpClient.GetAsync(versionedPath, ct).ConfigureAwait(false);
    }

    public async Task<HttpResponseMessage> PostAsync(
        string path, HttpContent content = null, CancellationToken ct = default)
    {
      var versionedPath = await GetVersionedPathAsync(path, ct).ConfigureAwait(false);
      var client = IsWaitEndpoint(versionedPath) ? _longRunningHttpClient : _httpClient;
      return await client.PostAsync(versionedPath, content, ct).ConfigureAwait(false);
    }

    public async Task<HttpResponseMessage> PutAsync(
        string path, HttpContent content, CancellationToken ct = default)
    {
      var versionedPath = await GetVersionedPathAsync(path, ct).ConfigureAwait(false);
      return await _httpClient.PutAsync(versionedPath, content, ct).ConfigureAwait(false);
    }

    public async Task<HttpResponseMessage> DeleteAsync(string path, CancellationToken ct = default)
    {
      var versionedPath = await GetVersionedPathAsync(path, ct).ConfigureAwait(false);
      return await _httpClient.DeleteAsync(versionedPath, ct).ConfigureAwait(false);
    }

    public async Task<Stream> GetStreamAsync(string path, CancellationToken ct = default)
    {
      var versionedPath = await GetVersionedPathAsync(path, ct).ConfigureAwait(false);
      var response = await _longRunningHttpClient.GetAsync(
          versionedPath, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
      await EnsureStreamSuccessAsync(response, ct).ConfigureAwait(false);
      var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
      return new ResponseOwningStream(stream, response);
    }

    public async Task<Stream> PostStreamAsync(
        string path, HttpContent content = null, CancellationToken ct = default)
    {
      return await PostStreamAsync(path, content, null, ct).ConfigureAwait(false);
    }

    public async Task<Stream> PostStreamAsync(
        string path, HttpContent content,
        IReadOnlyDictionary<string, string> headers, CancellationToken ct = default)
    {
      var versionedPath = await GetVersionedPathAsync(path, ct).ConfigureAwait(false);
      var request = new HttpRequestMessage(HttpMethod.Post, versionedPath) { Content = content };
      if (headers != null)
      {
        foreach (var header in headers)
          request.Headers.TryAddWithoutValidation(header.Key, header.Value);
      }
      var response = await _longRunningHttpClient.SendAsync(
          request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
      await EnsureStreamSuccessAsync(response, ct).ConfigureAwait(false);
      var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
      return new ResponseOwningStream(stream, response);
    }

    private static bool IsWaitEndpoint(string path) =>
        path.Contains("/containers/", StringComparison.Ordinal) &&
        path.EndsWith("/wait", StringComparison.Ordinal);

    /// <summary>
    /// Throws a descriptive <see cref="HttpRequestException"/> for a non-success stream
    /// response, preserving Docker's <c>{"message":"..."}</c> error body (bounded to 32 KiB)
    /// instead of discarding it like <c>EnsureSuccessStatusCode()</c> would. Disposes the
    /// response on failure so the connection is not leaked.
    /// </summary>
    private static async Task EnsureStreamSuccessAsync(
        HttpResponseMessage response, CancellationToken ct)
    {
      if (response.IsSuccessStatusCode)
        return;

      const int maxBytes = 32 * 1024;
      var body = string.Empty;
      try
      {
        var s = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var buffer = new byte[maxBytes];
        var total = 0;
        int read;
        while (total < maxBytes &&
               (read = await s.ReadAsync(buffer.AsMemory(total, maxBytes - total), ct).ConfigureAwait(false)) > 0)
          total += read;
        body = Encoding.UTF8.GetString(buffer, 0, total);
      }
      catch (OperationCanceledException) when (ct.IsCancellationRequested)
      {
        response.Dispose();
        throw;
      }
      catch (Exception)
      {
        // Body unavailable — fall back to the status line below.
      }

      var status = (int)response.StatusCode;
      var reason = response.ReasonPhrase;
      response.Dispose();

      var detail = ExtractDockerMessage(body)
          ?? (string.IsNullOrWhiteSpace(body) ? reason : body);
      throw new HttpRequestException($"Docker API {status}: {detail}");
    }

    private static string ExtractDockerMessage(string body)
    {
      if (string.IsNullOrWhiteSpace(body))
        return null;

      try
      {
        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.ValueKind == JsonValueKind.Object &&
            doc.RootElement.TryGetProperty("message", out var msg) &&
            msg.ValueKind == JsonValueKind.String)
          return msg.GetString();
      }
      catch (JsonException)
      {
        // Not the standard Docker error JSON — caller falls back to the raw body.
      }
      return null;
    }

    public async Task<bool> PingAsync(CancellationToken ct = default)
    {
      try
      {
        using var response = await _httpClient.GetAsync("/_ping", ct).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
      }
      catch (OperationCanceledException) when (ct.IsCancellationRequested)
      {
        // Cancellation requested by the CALLER's token is not a "ping failed" signal —
        // propagate it. An internal HttpClient.Timeout firing surfaces as a
        // TaskCanceledException whose token is NOT the caller's, so ct.IsCancellationRequested
        // is false there and that case falls through to the catch-all below (returns false).
        throw;
      }
      catch (Exception ex)
      {
        // Transport failure or an internal request-timeout — the endpoint is unreachable.
        _logger.LogError(ex, "Docker API ping failed");
        return false;
      }
    }

    public ValueTask DisposeAsync()
    {
      _negotiationLock.Dispose();
      _longRunningHttpClient.Dispose();
      _httpClient.Dispose();

      // Dispose any X509Certificate2 we created (client cert + custom CA) to release
      // their native handles. This runs AFTER _httpClient.Dispose() so the handler is no
      // longer using the client certificate, and the CA cert captured by the TLS
      // validation callback is no longer reachable. Disposing an X509Certificate2 twice
      // is a no-op, so this is safe to call again (idempotent dispose).
      foreach (var certificate in _ownedCertificates)
        certificate.Dispose();

      GC.SuppressFinalize(this);
      return ValueTask.CompletedTask;
    }

    private async Task<string> GetVersionedPathAsync(string path, CancellationToken ct)
    {
      var state = _negotiation;
      if (!state.Negotiated)
      {
        await _negotiationLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
          // Double-check after acquiring the lock.
          state = _negotiation;
          if (!state.Negotiated)
          {
            await NegotiateApiVersionAsync(ct).ConfigureAwait(false);
            state = _negotiation;
          }
        }
        finally
        {
          _negotiationLock.Release();
        }
      }

      return string.IsNullOrEmpty(state.ApiVersion)
          ? path
          : $"/v{state.ApiVersion}{path}";
    }

    private async Task NegotiateApiVersionAsync(CancellationToken ct)
    {
      try
      {
        using var response = await _httpClient.GetAsync("/_ping", ct).ConfigureAwait(false);
        string version = null;
        if (response.Headers.TryGetValues("API-Version", out var values))
        {
          foreach (var v in values)
          {
            version = v;
            break;
          }
        }

        // Atomically publish both the version and the negotiated flag.
        _negotiation = new NegotiationState(version, Negotiated: true);
      }
      catch (OperationCanceledException) when (ct.IsCancellationRequested)
      {
        // Caller cancellation is not a negotiation failure — propagate without caching, so a
        // later call retries negotiation instead of being stuck in a degraded "no version" mode.
        throw;
      }
      catch (Exception ex)
      {
        // Transient failure (daemon starting/unreachable). Do NOT cache as negotiated, so a later
        // request retries; this request proceeds without a version prefix.
        _logger.LogWarning(ex, "Docker API version negotiation failed; proceeding without version prefix (will retry)");
      }
    }

    internal static string GetDefaultHost()
    {
      return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
          ? "npipe:////./pipe/docker_engine"
          : "unix:///var/run/docker.sock";
    }

    // ownedCertificates collects every X509Certificate2 created here so the connection
    // instance can dispose them; the unix-socket and named-pipe paths add none.
    private static (SocketsHttpHandler handler, string baseAddress) CreateHandler(
        string host, DockerApiConnectionConfig config, List<X509Certificate2> ownedCertificates)
    {
      var uri = new Uri(host);

      return uri.Scheme.ToLowerInvariant() switch
      {
        "unix" => CreateUnixSocketHandler(uri, config),
        "npipe" => CreateNamedPipeHandler(uri, config),
        "tcp" or "http" => CreateTcpHandler(uri, config, useTls: false, ownedCertificates),
        "https" => CreateTcpHandler(uri, config, useTls: true, ownedCertificates),
        _ => throw new ArgumentException($"Unsupported URI scheme: {uri.Scheme}. " +
            "Use unix://, npipe://, tcp://, or https://", nameof(host))
      };
    }

    private static (SocketsHttpHandler, string) CreateUnixSocketHandler(
        Uri uri, DockerApiConnectionConfig config)
    {
      var socketPath = uri.AbsolutePath;
      var handler = new SocketsHttpHandler
      {
        ConnectCallback = async (context, ct) =>
        {
          var socket = new Socket(
                      AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
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

      return (handler, "http://localhost");
    }

    private static (SocketsHttpHandler, string) CreateNamedPipeHandler(
        Uri uri, DockerApiConnectionConfig config)
    {
      var pipeName = uri.AbsolutePath.TrimStart('/');
      var handler = new SocketsHttpHandler
      {
        ConnectCallback = async (_, ct) =>
        {
          var pipe = new NamedPipeClientStream(
                      ".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
          try
          {
            await pipe.ConnectAsync((int)config.ConnectionTimeout.TotalMilliseconds, ct).ConfigureAwait(false);
            return pipe;
          }
          catch
          {
            pipe.Dispose();
            throw;
          }
        },
        ConnectTimeout = config.ConnectionTimeout
      };

      return (handler, "http://localhost");
    }

    // Every X509Certificate2 created here is added to ownedCertificates so the connection
    // instance can dispose them (they own native handles); they outlive this method because
    // the client cert is referenced by the handler and the CA cert is captured by the
    // validation callback below.
    private static (SocketsHttpHandler, string) CreateTcpHandler(
        Uri uri, DockerApiConnectionConfig config, bool useTls, List<X509Certificate2> ownedCertificates)
    {
      var handler = new SocketsHttpHandler
      {
        ConnectTimeout = config.ConnectionTimeout
      };

      var hasCerts = !string.IsNullOrEmpty(config.CertificatePath);

      if (useTls || hasCerts)
      {
        var sslOptions = new SslClientAuthenticationOptions();

        if (hasCerts)
        {
          var certPath = Path.Combine(config.CertificatePath, "cert.pem");
          var keyPath = Path.Combine(config.CertificatePath, "key.pem");

          if (File.Exists(certPath) && File.Exists(keyPath))
          {
            var clientCert = X509Certificate2.CreateFromPemFile(certPath, keyPath);
            ownedCertificates.Add(clientCert);
            sslOptions.ClientCertificates = [clientCert];
          }

          if (!config.VerifyTls)
          {
#pragma warning disable CA5359 // Intentional: user opted out of TLS verification via VerifyTls=false
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
              var caCert = X509Certificate2.CreateFromPem(File.ReadAllText(caPath));
#endif
              ownedCertificates.Add(caCert);
              sslOptions.RemoteCertificateValidationCallback = (_, cert, chain, errors) =>
                  ModelTlsValidation.ValidateWithCustomRoot(caCert, cert, chain, errors, config.AllowTlsHostnameMismatch);
            }
            else if (config.AllowTlsHostnameMismatch)
            {
              sslOptions.RemoteCertificateValidationCallback = (_, _, _, errors) =>
                  errors is SslPolicyErrors.None or SslPolicyErrors.RemoteCertificateNameMismatch;
            }
          }
        }
        else if (!config.VerifyTls)
        {
#pragma warning disable CA5359 // Intentional: user opted out of TLS verification via VerifyTls=false
          sslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;
#pragma warning restore CA5359
        }
        else if (config.AllowTlsHostnameMismatch)
        {
          sslOptions.RemoteCertificateValidationCallback = (_, _, _, errors) =>
              errors is SslPolicyErrors.None or SslPolicyErrors.RemoteCertificateNameMismatch;
        }

        handler.SslOptions = sslOptions;
      }

      var scheme = (useTls || hasCerts) ? "https" : "http";
      var port = uri.Port > 0 ? uri.Port : (useTls ? 2376 : 2375);
      var baseAddress = $"{scheme}://{uri.Host}:{port}";

      return (handler, baseAddress);
    }
  }
}
