using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Security;
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
    // Shared in-flight negotiation: concurrent un-negotiated requests JOIN one attempt
    // instead of each running its own /_ping serially behind the lock (a daemon outage
    // otherwise costs request N about N x ConnectionTimeout). Guarded by _negotiationLock.
    private Task _negotiationTask;
    // Negative cache for the terminal unsupported-daemon-version failure: re-probing an
    // incompatible daemon on every request is pointless, but a cooldown (not a permanent
    // cache) lets the client recover when the daemon is live-upgraded. Guarded by _negotiationLock.
    private volatile DriverException _unsupportedDaemonFailure;
    private long _unsupportedDaemonTimestamp;
    private static readonly TimeSpan UnsupportedDaemonRetryCooldown = TimeSpan.FromSeconds(30);
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

    /// <summary>Creates a Docker API connection using the supplied transport configuration.</summary>
    public DockerApiConnection(DockerApiConnectionConfig config, ILoggerFactory loggerFactory = null)
    {
      ArgumentNullException.ThrowIfNull(config);
      _config = CreateEffectiveConfig(config);
      // A non-positive-but-finite ConnectionTimeout is a misconfiguration: it bounds the upload stall
      // watchdog, so a zero/negative value would make it cancel every body-bearing upload almost
      // immediately. Accept any positive duration or Timeout.InfiniteTimeSpan (disables the stall
      // watchdog), mirroring DockerApiDriverBuilder.WithConnectionTimeout (which cannot express
      // "infinite") (DAPI-3).
      ValidateConnectionTimeout(_config.ConnectionTimeout);
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

    /// <inheritdoc />
    public string ApiVersion => _negotiation.ApiVersion;

    /// <summary>
    /// Whether API version negotiation has completed (either via config or ping).
    /// </summary>
    public bool IsVersionNegotiated => _negotiation.Negotiated;

    /// <inheritdoc />
    public async Task<HttpResponseMessage> GetAsync(string path, CancellationToken ct = default)
    {
      ThrowIfDisposed();
      var versionedPath = await GetVersionedPathAsync(path, ct).ConfigureAwait(false);
      return await _httpClient.GetAsync(versionedPath, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<HttpResponseMessage> HeadAsync(string path, CancellationToken ct = default)
    {
      ThrowIfDisposed();
      var versionedPath = await GetVersionedPathAsync(path, ct).ConfigureAwait(false);
      using var request = new HttpRequestMessage(HttpMethod.Head, versionedPath);
      return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
          .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<HttpResponseMessage> PostAsync(
        string path, HttpContent content = null, CancellationToken ct = default)
    {
      ThrowIfDisposed();
      var versionedPath = await GetVersionedPathAsync(path, ct).ConfigureAwait(false);
      var client = UseLongRunningPostClient(versionedPath) ? _longRunningHttpClient : _httpClient;
      return await client.PostAsync(versionedPath, content, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<HttpResponseMessage> PutAsync(
        string path, HttpContent content, CancellationToken ct = default)
    {
      ThrowIfDisposed();
      var versionedPath = await GetVersionedPathAsync(path, ct).ConfigureAwait(false);
      // Archive uploads (PUT /containers/{id}/archive) can legitimately stream multi-GB
      // bodies for longer than the buffered client's whole-request timeout. Send via the
      // upload watchdog: the body phase is bounded by write-progress stalls
      // (ConnectionTimeout of no progress), not wall clock, and the response-header wait by
      // RequestTimeout — the same protection the heavy POST uploads get.
      using var request = new HttpRequestMessage(HttpMethod.Put, versionedPath) { Content = content };
      return await SendForHeadersAsync(request, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<HttpResponseMessage> DeleteAsync(string path, CancellationToken ct = default)
    {
      ThrowIfDisposed();
      var versionedPath = await GetVersionedPathAsync(path, ct).ConfigureAwait(false);
      return await _httpClient.DeleteAsync(versionedPath, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Stream> GetStreamAsync(string path, CancellationToken ct = default)
    {
      ThrowIfDisposed();
      var versionedPath = await GetVersionedPathAsync(path, ct).ConfigureAwait(false);
      using var request = new HttpRequestMessage(HttpMethod.Get, versionedPath);
      HttpResponseMessage response = null;
      var transferred = false;
      try
      {
        response = await SendForHeadersAsync(request, ct).ConfigureAwait(false);
        await EnsureStreamSuccessAsync(response, ct).ConfigureAwait(false);
        var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        transferred = true;
        return CreateResponseStream(stream, response);
      }
      catch
      {
        if (!transferred)
          response?.Dispose();
        throw;
      }
    }

    /// <inheritdoc />
    public async Task<Stream> PostStreamAsync(
        string path, HttpContent content = null, CancellationToken ct = default)
    {
      return await PostStreamAsync(path, content, null, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
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
      HttpResponseMessage response = null;
      var transferred = false;
      try
      {
        response = await SendForHeadersAsync(request, ct).ConfigureAwait(false);
        await EnsureStreamSuccessAsync(response, ct).ConfigureAwait(false);
        var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        transferred = true;
        return CreateResponseStream(stream, response);
      }
      catch
      {
        if (!transferred)
          response?.Dispose();
        throw;
      }
    }

    /// <summary>
    /// Throws a descriptive <see cref="HttpRequestException"/> for a non-success stream
    /// response, preserving Docker's <c>{"message":"..."}</c> error body (bounded to 32 KiB,
    /// and in time by <see cref="DockerApiConnectionConfig.ConnectionTimeout"/>) instead of
    /// discarding it like <c>EnsureSuccessStatusCode()</c> would. Disposes the response on
    /// failure so the connection is not leaked.
    /// </summary>
    private async Task EnsureStreamSuccessAsync(
        HttpResponseMessage response, CancellationToken ct)
    {
      if (response.IsSuccessStatusCode)
        return;

      const int maxBytes = 32 * 1024;
      var buffer = new byte[maxBytes];
      var total = 0;
      try
      {
        // The TTFB watchdog bounds only the header phase; a daemon that sends the non-2xx
        // status line and then stalls would otherwise keep this body read pending forever
        // under CancellationToken.None. Bound it by ConnectionTimeout; on timeout proceed
        // with whatever was read — the HTTP status is the primary signal, the body only
        // enriches the message.
        using var bodyCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        bodyCts.CancelAfter(_config.ConnectionTimeout);
        var s = await response.Content.ReadAsStreamAsync(bodyCts.Token).ConfigureAwait(false);
        int read;
        while (total < maxBytes &&
               (read = await s.ReadAsync(buffer.AsMemory(total, maxBytes - total), bodyCts.Token).ConfigureAwait(false)) > 0)
          total += read;
      }
      catch (OperationCanceledException) when (ct.IsCancellationRequested)
      {
        response.Dispose();
        throw;
      }
      catch (Exception)
      {
        // Body unavailable or the bounded read timed out — keep the partial body (if any)
        // and fall back to the status line below.
      }
      var body = Encoding.UTF8.GetString(buffer, 0, total);

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

    /// <inheritdoc />
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

    /// <inheritdoc />
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
    /// Negotiation is retried on every request until it succeeds, favoring fast recovery when
    /// the daemon returns. Concurrent un-negotiated requests share ONE in-flight negotiation
    /// attempt (each still honors its own cancellation token) so a daemon outage costs the
    /// whole batch one <c>ConnectionTimeout</c>, not one per queued request. The terminal
    /// unsupported-daemon-version failure is negatively cached for a short cooldown.
    /// </remarks>
    private async Task<string> GetVersionedPathAsync(string path, CancellationToken ct)
    {
      ThrowIfDisposed();
      var state = _negotiation;
      if (!state.Negotiated)
      {
        Task negotiation = null;
        await _negotiationLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
          ThrowIfDisposed();
          // Double-check after acquiring the lock.
          state = _negotiation;
          if (!state.Negotiated)
          {
            if (_unsupportedDaemonFailure != null &&
                Stopwatch.GetElapsedTime(_unsupportedDaemonTimestamp) < UnsupportedDaemonRetryCooldown)
            {
              // Chain (not rethrow) the cached instance so each surfaced exception owns its
              // stack trace while keeping the dedicated error code.
              throw new DriverException(
                  _unsupportedDaemonFailure.Message, _unsupportedDaemonFailure.ErrorCode,
                  context: null, _unsupportedDaemonFailure, isTransient: false);
            }

            if (_negotiationTask is not { IsCompleted: false })
            {
              var attempt = NegotiateAndRecordAsync();
              // Observe the fault out-of-band: if every joiner cancels before awaiting, the
              // typed failure must not surface as an UnobservedTaskException.
              _ = attempt.ContinueWith(
                  t => _ = t.Exception,
                  CancellationToken.None,
                  TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                  TaskScheduler.Default);
              _negotiationTask = attempt;
            }

            negotiation = _negotiationTask;
          }
        }
        finally
        {
          _negotiationLock.Release();
        }

        if (negotiation != null)
        {
          // Join the shared attempt; WaitAsync honors THIS caller's token without
          // cancelling the shared work (which is bounded internally by ConnectionTimeout).
          await negotiation.WaitAsync(ct).ConfigureAwait(false);
          state = _negotiation;
        }
      }

      return string.IsNullOrEmpty(state.ApiVersion)
          ? path
          : $"/v{state.ApiVersion}{path}";
    }

    /// <summary>
    /// Runs one negotiation attempt detached from any single caller's cancellation and
    /// records/clears the unsupported-daemon negative cache.
    /// </summary>
    private async Task NegotiateAndRecordAsync()
    {
      try
      {
        await NegotiateApiVersionAsync(CancellationToken.None).ConfigureAwait(false);
        _unsupportedDaemonFailure = null;
      }
      catch (DriverException ex) when (
          string.Equals(ex.ErrorCode, Model.Drivers.ErrorCodes.Api.UnsupportedVersion, StringComparison.Ordinal))
      {
        // Timestamp first: the volatile write of the exception field publishes it.
        _unsupportedDaemonTimestamp = Stopwatch.GetTimestamp();
        _unsupportedDaemonFailure = ex;
        throw;
      }
    }

    private void ThrowIfDisposed()
    {
      ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    }

    internal static string GetDefaultHost()
    {
      return DockerUri.GetDockerHostEnvironmentPathOrDefault();
    }

    // Accept any strictly-positive duration, or Timeout.InfiniteTimeSpan (-1 ms) which disables the
    // upload stall watchdog. A zero or negative-but-finite value is rejected because it would make the
    // watchdog cancel every body-bearing upload almost immediately (DAPI-3).
    private static void ValidateConnectionTimeout(TimeSpan timeout)
    {
      if (timeout != Timeout.InfiniteTimeSpan && timeout <= TimeSpan.Zero)
        throw new ArgumentOutOfRangeException(
            nameof(timeout), timeout,
            "ConnectionTimeout must be positive or Timeout.InfiniteTimeSpan.");
    }

    private static DockerApiConnectionConfig CreateEffectiveConfig(DockerApiConnectionConfig config)
    {
      // Docker convention: DOCKER_TLS_VERIFY set to any non-empty value (even "0") ENABLES
      // verification. Applying it via OR means the environment can only strengthen verification,
      // never weaken an explicit VerifyTls=true, so a stray env var can't silently open the
      // connection to a man-in-the-middle.
      var tlsVerify = Environment.GetEnvironmentVariable("DOCKER_TLS_VERIFY");
      var explicitCertificatePath = !string.IsNullOrEmpty(config.CertificatePath);
      return new DockerApiConnectionConfig
      {
        Host = config.Host ?? DockerUri.GetDockerHostEnvironmentPathOrDefault(),
        CertificatePath = config.CertificatePath ?? Environment.GetEnvironmentVariable("DOCKER_CERT_PATH"),
        VerifyTls = config.VerifyTls || !string.IsNullOrEmpty(tlsVerify),
        ConnectionTimeout = config.ConnectionTimeout,
        RequestTimeout = config.RequestTimeout,
        StreamIdleTimeout = config.StreamIdleTimeout,
        ApiVersion = config.ApiVersion,
        AllowTlsHostnameMismatch = config.AllowTlsHostnameMismatch,
        UseTls = explicitCertificatePath || !string.IsNullOrEmpty(tlsVerify)
      };
    }

  }
}
