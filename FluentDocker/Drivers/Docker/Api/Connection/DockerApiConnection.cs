using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers.Models.Connection;
using FluentDocker.Model.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ResponseOwningStream = FluentDocker.Drivers.Connection.ResponseOwningStream;

namespace FluentDocker.Drivers.Docker.Api.Connection
{
  /// <summary>
  /// HTTP connection to the Docker Engine REST API.
  /// Supports Unix domain sockets, Windows named pipes, and TCP with optional TLS.
  /// </summary>
  public sealed partial class DockerApiConnection : IDockerApiConnection
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
    private int _disposed;

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
      _config = CreateEffectiveConfig(config);
      _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<DockerApiConnection>();

      var ownedCertificates = new List<X509Certificate2>();
      SocketsHttpHandler handler;
      string baseAddress;
      try
      {
        var host = _config.Host;
        (handler, baseAddress) = CreateHandler(host, _config, ownedCertificates);
        _ownedCertificates = ownedCertificates;
      }
      catch
      {
        foreach (var certificate in ownedCertificates)
          certificate.Dispose();
        throw;
      }

      var baseUri = new Uri(baseAddress);
      _httpClient = new HttpClient(handler, disposeHandler: true)
      {
        BaseAddress = baseUri,
        Timeout = _config.RequestTimeout
      };
      _longRunningHttpClient = new HttpClient(handler, disposeHandler: false)
      {
        BaseAddress = baseUri,
        Timeout = Timeout.InfiniteTimeSpan
      };

      // If the user pre-set ApiVersion, mark negotiation as already done.
      _negotiation = !string.IsNullOrEmpty(_config.ApiVersion)
          ? new NegotiationState(_config.ApiVersion, Negotiated: true)
          : new NegotiationState(null, Negotiated: false);
    }

    public string ApiVersion => _negotiation.ApiVersion;

    /// <summary>
    /// Whether API version negotiation has completed (either via config or ping).
    /// </summary>
    public bool IsVersionNegotiated => _negotiation.Negotiated;

    public async Task<HttpResponseMessage> GetAsync(string path, CancellationToken ct = default)
    {
      ThrowIfDisposed();
      var versionedPath = await GetVersionedPathAsync(path, ct).ConfigureAwait(false);
      return await _httpClient.GetAsync(versionedPath, ct).ConfigureAwait(false);
    }

    public async Task<HttpResponseMessage> HeadAsync(string path, CancellationToken ct = default)
    {
      ThrowIfDisposed();
      var versionedPath = await GetVersionedPathAsync(path, ct).ConfigureAwait(false);
      using var request = new HttpRequestMessage(HttpMethod.Head, versionedPath);
      return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
          .ConfigureAwait(false);
    }

    public async Task<HttpResponseMessage> PostAsync(
        string path, HttpContent content = null, CancellationToken ct = default)
    {
      ThrowIfDisposed();
      var versionedPath = await GetVersionedPathAsync(path, ct).ConfigureAwait(false);
      var client = UseLongRunningPostClient(versionedPath) ? _longRunningHttpClient : _httpClient;
      return await client.PostAsync(versionedPath, content, ct).ConfigureAwait(false);
    }

    public async Task<HttpResponseMessage> PutAsync(
        string path, HttpContent content, CancellationToken ct = default)
    {
      ThrowIfDisposed();
      var versionedPath = await GetVersionedPathAsync(path, ct).ConfigureAwait(false);
      return await _httpClient.PutAsync(versionedPath, content, ct).ConfigureAwait(false);
    }

    public async Task<HttpResponseMessage> DeleteAsync(string path, CancellationToken ct = default)
    {
      ThrowIfDisposed();
      var versionedPath = await GetVersionedPathAsync(path, ct).ConfigureAwait(false);
      return await _httpClient.DeleteAsync(versionedPath, ct).ConfigureAwait(false);
    }

    public async Task<Stream> GetStreamAsync(string path, CancellationToken ct = default)
    {
      ThrowIfDisposed();
      var versionedPath = await GetVersionedPathAsync(path, ct).ConfigureAwait(false);
      using var request = new HttpRequestMessage(HttpMethod.Get, versionedPath);
      var response = await SendForHeadersAsync(request, ct).ConfigureAwait(false);
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
      ThrowIfDisposed();
      var versionedPath = await GetVersionedPathAsync(path, ct).ConfigureAwait(false);
      using var request = new HttpRequestMessage(HttpMethod.Post, versionedPath) { Content = content };
      if (headers != null)
      {
        foreach (var header in headers)
          request.Headers.TryAddWithoutValidation(header.Key, header.Value);
      }
      var response = await SendForHeadersAsync(request, ct).ConfigureAwait(false);
      await EnsureStreamSuccessAsync(response, ct).ConfigureAwait(false);
      var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
      return new ResponseOwningStream(stream, response);
    }

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

      var statusEnum = response.StatusCode;
      var status = (int)statusEnum;
      var reason = response.ReasonPhrase;
      response.Dispose();

      var detail = ExtractDockerMessage(body)
          ?? (string.IsNullOrWhiteSpace(body) ? reason : body);
      throw new HttpRequestException(
          $"Docker API {status}: {detail}", null, statusEnum);
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
      ThrowIfDisposed();
      try
      {
        using var pingCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        pingCts.CancelAfter(TimeSpan.FromSeconds(5));
        using var response = await _httpClient.GetAsync("/_ping", pingCts.Token).ConfigureAwait(false);
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

    public async ValueTask DisposeAsync()
    {
      if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        return;

      var lockTaken = await _negotiationLock
          .WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None)
          .ConfigureAwait(false);
      try
      {
        _longRunningHttpClient.Dispose();
        _httpClient.Dispose();

        // Dispose any X509Certificate2 we created (client cert + custom CA) to release
        // their native handles. This runs AFTER _httpClient.Dispose() so the handler is no
        // longer using the client certificate, and the CA cert captured by the TLS
        // validation callback is no longer reachable.
        foreach (var certificate in _ownedCertificates)
          certificate.Dispose();
      }
      finally
      {
        // The SemaphoreSlim is deliberately not disposed: it holds no unmanaged state
        // (AvailableWaitHandle is never touched), and disposing it races in-flight
        // waiters into ObjectDisposedException from their finally-Release.
        if (lockTaken)
          _negotiationLock.Release();
      }

      GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Ensures the request path carries the negotiated Docker API version.
    /// </summary>
    /// <remarks>
    /// Negotiation is retried on every request until it succeeds. That favors fast recovery
    /// when the daemon returns, at the cost of one extra <c>/_ping</c> per request during a
    /// total outage; a negative-cache cooldown is deliberately omitted.
    /// </remarks>
    private async Task<string> GetVersionedPathAsync(string path, CancellationToken ct)
    {
      ThrowIfDisposed();
      var state = _negotiation;
      if (!state.Negotiated)
      {
        await _negotiationLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
          ThrowIfDisposed();
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

    private void ThrowIfDisposed()
    {
      ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
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
      return DockerUri.GetDockerHostEnvironmentPathOrDefault();
    }

    private static DockerApiConnectionConfig CreateEffectiveConfig(DockerApiConnectionConfig config)
    {
      // Docker convention: DOCKER_TLS_VERIFY set to any non-empty value (even "0") ENABLES
      // verification. Applying it via OR means the environment can only strengthen verification,
      // never weaken an explicit VerifyTls=true, so a stray env var can't silently open the
      // connection to a man-in-the-middle.
      var tlsVerify = Environment.GetEnvironmentVariable("DOCKER_TLS_VERIFY");
      return new DockerApiConnectionConfig
      {
        Host = config.Host ?? DockerUri.GetDockerHostEnvironmentPathOrDefault(),
        CertificatePath = config.CertificatePath ?? Environment.GetEnvironmentVariable("DOCKER_CERT_PATH"),
        VerifyTls = config.VerifyTls || !string.IsNullOrEmpty(tlsVerify),
        ConnectionTimeout = config.ConnectionTimeout,
        RequestTimeout = config.RequestTimeout,
        ApiVersion = config.ApiVersion,
        AllowTlsHostnameMismatch = config.AllowTlsHostnameMismatch
      };
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
      var socketPath = Uri.UnescapeDataString(uri.AbsolutePath);
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
      if (!string.IsNullOrEmpty(uri.Host) &&
          !string.Equals(uri.Host, ".", StringComparison.Ordinal) &&
          !string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
        throw new ArgumentException(
            "Remote Docker named pipes are not supported; use tcp:// or https:// for remote daemons.",
            nameof(config));

      var pipeName = ExtractNamedPipeName(uri);
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

    private static string ExtractNamedPipeName(Uri uri)
    {
      var path = uri.AbsolutePath.Trim('/');
      if (path.StartsWith("./", StringComparison.Ordinal))
        path = path[2..];
      if (path.StartsWith("pipe/", StringComparison.OrdinalIgnoreCase))
        path = path["pipe/".Length..];
      path = path.Trim('/');
      if (string.IsNullOrEmpty(path))
        throw new ArgumentException("Named pipe URI must include a pipe name.", nameof(uri));
      return Uri.UnescapeDataString(path).Replace('/', '\\');
    }

  }
}
