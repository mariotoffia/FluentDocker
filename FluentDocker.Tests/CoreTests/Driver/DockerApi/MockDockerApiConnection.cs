using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers.Docker.Api.Connection;

namespace FluentDocker.Tests.CoreTests.Driver.DockerApi
{
  /// <summary>
  /// A recorded request captured by <see cref="MockDockerApiConnection"/>.
  /// </summary>
  public sealed record CapturedRequest(
      string Method,
      string Path,
      string? Body,
      IReadOnlyDictionary<string, string>? Headers = null,
      byte[]? BodyBytes = null);

  /// <summary>
  /// In-memory mock of <see cref="IDockerApiConnection"/> that returns canned
  /// responses and records every request for later verification.
  /// </summary>
  public sealed class MockDockerApiConnection : IDockerApiConnection
  {
    private readonly record struct ResponseEntry(
        string Method,
        string PathContains,
        HttpStatusCode StatusCode,
        string? JsonBody,
        string? StreamContent,
        byte[]? StreamBytes,
        Exception? StreamException);

    private readonly List<ResponseEntry> _entries = [];
    private readonly List<(string PathContains, HttpStatusCode StatusCode, IReadOnlyDictionary<string, string> Headers)> _headEntries = [];
    private readonly List<(string PathContains, Func<Stream> Factory)> _streamFactories = [];
    private readonly List<CapturedRequest> _requests = [];
    private readonly List<TrackingContent> _contents = [];
    private readonly List<RecordingStream> _streams = [];
    private bool _pingSuccess = true;

    public string ApiVersion { get; set; } = "1.45";

    /// <summary>Every stream handed out by the mock, in order, so tests can assert disposal.</summary>
    public IReadOnlyList<RecordingStream> Streams => _streams;
    public IReadOnlyList<TrackingContent> Contents => _contents;

    // ── Setup (fluent) ──────────────────────────────────────────────

    public MockDockerApiConnection SetupGet(
        string pathContains, int statusCode, string jsonBody)
    {
      _entries.Add(new ResponseEntry(
          "GET", pathContains, (HttpStatusCode)statusCode, jsonBody, null, null, null));
      return this;
    }

    public MockDockerApiConnection SetupPost(
        string pathContains, int statusCode, string jsonBody)
    {
      _entries.Add(new ResponseEntry(
          "POST", pathContains, (HttpStatusCode)statusCode, jsonBody, null, null, null));
      return this;
    }

    public MockDockerApiConnection SetupPut(
        string pathContains, int statusCode, string jsonBody)
    {
      _entries.Add(new ResponseEntry(
          "PUT", pathContains, (HttpStatusCode)statusCode, jsonBody, null, null, null));
      return this;
    }

    public MockDockerApiConnection SetupDelete(
        string pathContains, int statusCode, string jsonBody)
    {
      _entries.Add(new ResponseEntry(
          "DELETE", pathContains, (HttpStatusCode)statusCode, jsonBody, null, null, null));
      return this;
    }

    public MockDockerApiConnection SetupHead(
        string pathContains, int statusCode, IReadOnlyDictionary<string, string> headers)
    {
      _headEntries.Add((pathContains, (HttpStatusCode)statusCode, headers));
      return this;
    }

    public MockDockerApiConnection SetupStream(
        string pathContains, string streamContent)
    {
      _entries.Add(new ResponseEntry(
          "STREAM", pathContains, HttpStatusCode.OK, null, streamContent, null, null));
      return this;
    }

    /// <summary>
    /// Sets up a stream endpoint that returns raw binary content.
    /// Useful for testing multiplexed frame parsing.
    /// </summary>
    public MockDockerApiConnection SetupStreamBytes(
        string pathContains, byte[] bytes)
    {
      _entries.Add(new ResponseEntry(
          "STREAM", pathContains, HttpStatusCode.OK, null, null, bytes, null));
      return this;
    }

    /// <summary>
    /// Sets up a stream endpoint that throws when opened, simulating a stream-open
    /// failure (e.g. a non-success status surfaced by the real connection).
    /// </summary>
    public MockDockerApiConnection SetupStreamThrows(
        string pathContains, Exception ex)
    {
      _entries.Add(new ResponseEntry(
          "STREAM_THROW", pathContains, HttpStatusCode.OK, null, null, null, ex));
      return this;
    }

    /// <summary>
    /// Sets up a stream endpoint whose stream yields <paramref name="prefix"/> bytes and then
    /// throws <paramref name="ex"/> on the next read, simulating a mid-stream read failure.
    /// </summary>
    public MockDockerApiConnection SetupStreamReadThrows(
        string pathContains, byte[] prefix, Exception ex)
    {
      _entries.Add(new ResponseEntry(
          "STREAM_READ_THROW", pathContains, HttpStatusCode.OK, null, null, prefix, ex));
      return this;
    }

    /// <summary>
    /// Sets up a stream endpoint whose stream is produced by <paramref name="factory"/>,
    /// for tests needing full control over read timing (e.g. gated blocking streams).
    /// Takes precedence over other stream setups for matching paths.
    /// </summary>
    public MockDockerApiConnection SetupStreamFactory(
        string pathContains, Func<Stream> factory)
    {
      _streamFactories.Add((pathContains, factory));
      return this;
    }

    public MockDockerApiConnection SetupPing(bool success)
    {
      _pingSuccess = success;
      return this;
    }

    // ── Verification ────────────────────────────────────────────────

    /// <summary>Returns every request captured so far, in order.</summary>
    public IReadOnlyList<CapturedRequest> GetRequests() => _requests;

    // ── IDockerApiConnection ────────────────────────────────────────

    public Task<HttpResponseMessage> GetAsync(
        string path, CancellationToken ct = default)
    {
      Record("GET", path, null);
      return Task.FromResult(Resolve("GET", path));
    }

    public Task<HttpResponseMessage> HeadAsync(
        string path, CancellationToken ct = default)
    {
      Record("HEAD", path, null);
      return Task.FromResult(ResolveHead(path));
    }

    public async Task<HttpResponseMessage> PostAsync(
        string path, HttpContent? content = null, CancellationToken ct = default)
    {
      var (body, bodyBytes) = await ReadContentAsync(content, ct).ConfigureAwait(false);

      Record("POST", path, body, bodyBytes: bodyBytes);
      return Resolve("POST", path);
    }

    public async Task<HttpResponseMessage> PostAsync(
        string path, HttpContent? content,
        IReadOnlyDictionary<string, string> headers, CancellationToken ct = default)
    {
      var (body, bodyBytes) = await ReadContentAsync(content, ct).ConfigureAwait(false);

      Record("POST", path, body, headers, bodyBytes);
      return Resolve("POST", path);
    }

    public async Task<HttpResponseMessage> PutAsync(
        string path, HttpContent content, CancellationToken ct = default)
    {
      var (body, bodyBytes) = await ReadContentAsync(content, ct).ConfigureAwait(false);

      Record("PUT", path, body, bodyBytes: bodyBytes);
      return Resolve("PUT", path);
    }

    public Task<HttpResponseMessage> DeleteAsync(
        string path, CancellationToken ct = default)
    {
      Record("DELETE", path, null);
      return Task.FromResult(Resolve("DELETE", path));
    }

    public Task<Stream> GetStreamAsync(
        string path, CancellationToken ct = default)
    {
      Record("GET_STREAM", path, null);
      return Task.FromResult(ResolveStream(path));
    }

    public async Task<Stream> PostStreamAsync(
        string path, HttpContent? content = null, CancellationToken ct = default)
    {
      var (body, bodyBytes) = await ReadContentAsync(content, ct).ConfigureAwait(false);
      Record("POST_STREAM", path, body, bodyBytes: bodyBytes);
      return ResolveStream(path);
    }

    public async Task<Stream> PostStreamAsync(
        string path, HttpContent? content,
        IReadOnlyDictionary<string, string> headers, CancellationToken ct = default)
    {
      var (body, bodyBytes) = await ReadContentAsync(content, ct).ConfigureAwait(false);
      Record("POST_STREAM", path, body, headers, bodyBytes);
      return ResolveStream(path);
    }

    public Task<bool> PingAsync(CancellationToken ct = default)
    {
      Record("PING", "/_ping", null);
      return Task.FromResult(_pingSuccess);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // ── Internals ───────────────────────────────────────────────────

    private void Record(
        string method, string path, string? body,
        IReadOnlyDictionary<string, string>? headers = null,
        byte[]? bodyBytes = null)
    {
      _requests.Add(new CapturedRequest(method, path, body, headers, bodyBytes));
    }

    private static async Task<(string? Body, byte[]? BodyBytes)> ReadContentAsync(
        HttpContent? content, CancellationToken ct)
    {
      if (content is null)
        return (null, null);

      var bytes = await content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
      return (Encoding.UTF8.GetString(bytes), bytes);
    }

    private HttpResponseMessage Resolve(string method, string path)
    {
      var entry = _entries
          .Where(e => e.Method == method && path.Contains(e.PathContains))
          .LastOrDefault();

      if (entry == default)
      {
        return new HttpResponseMessage(HttpStatusCode.NotFound)
        {
          Content = new StringContent(
                $"{{\"message\":\"no mock for {method} {path}\"}}",
                Encoding.UTF8, "application/json")
        };
      }

      var content = new TrackingContent(
          entry.JsonBody ?? "{}", Encoding.UTF8, "application/json");
      _contents.Add(content);
      return new HttpResponseMessage(entry.StatusCode)
      {
        Content = content
      };
    }

    private HttpResponseMessage ResolveHead(string path)
    {
      var entry = _headEntries
          .Where(e => path.Contains(e.PathContains))
          .LastOrDefault();

      if (entry == default)
        return new HttpResponseMessage(HttpStatusCode.NotFound);

      var response = new HttpResponseMessage(entry.StatusCode);
      foreach (var (name, value) in entry.Headers)
        response.Headers.TryAddWithoutValidation(name, value);
      return response;
    }

#pragma warning disable CA1859 // return type must be Stream for Task<Stream> callers
    private Stream ResolveStream(string path)
#pragma warning restore CA1859
    {
      var factory = _streamFactories
          .Where(f => path.Contains(f.PathContains))
          .Select(f => f.Factory)
          .LastOrDefault();
      if (factory != null)
        return factory();

      // A STREAM_THROW entry simulates a stream-open failure.
      var throwEntry = _entries
          .Where(e => e.Method == "STREAM_THROW" && path.Contains(e.PathContains))
          .LastOrDefault();
      if (throwEntry != default && throwEntry.StreamException != null)
        throw throwEntry.StreamException;

      // A STREAM_READ_THROW entry yields a byte prefix then throws on the next read.
      var readThrowEntry = _entries
          .Where(e => e.Method == "STREAM_READ_THROW" && path.Contains(e.PathContains))
          .LastOrDefault();
      if (readThrowEntry != default && readThrowEntry.StreamException != null)
        return new ThrowingReadStream(
            readThrowEntry.StreamBytes ?? Array.Empty<byte>(), readThrowEntry.StreamException);

      var entry = _entries
          .Where(e => e.Method == "STREAM" && path.Contains(e.PathContains))
          .LastOrDefault();

      if (entry != default && entry.StreamBytes != null)
        return Record(new RecordingStream(entry.StreamBytes));

      var text = entry == default ? string.Empty : entry.StreamContent ?? string.Empty;
      return Record(new RecordingStream(Encoding.UTF8.GetBytes(text)));
    }

    private RecordingStream Record(RecordingStream stream)
    {
      _streams.Add(stream);
      return stream;
    }
  }

  /// <summary>
  /// Serves a fixed prefix on the first read, signals, then blocks forever on
  /// subsequent reads until the read's own cancellation token fires.
  /// </summary>
  public sealed class GatedTailStream(byte[] prefix, TaskCompletionSource served) : Stream
  {
    private bool _prefixServed;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
      get => throw new NotSupportedException();
      set => throw new NotSupportedException();
    }

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
      if (!_prefixServed)
      {
        _prefixServed = true;
        prefix.CopyTo(buffer);
        served.TrySetResult();
        return prefix.Length;
      }

      await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
      return 0;
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
  }

  /// <summary>A MemoryStream that records whether it was disposed, so tests can assert resource cleanup.</summary>
  public sealed class RecordingStream : MemoryStream
  {
    public RecordingStream(byte[] buffer) : base(buffer)
    {
    }

    public bool IsDisposed { get; private set; }

    protected override void Dispose(bool disposing)
    {
      IsDisposed = true;
      base.Dispose(disposing);
    }
  }

  public sealed class TrackingContent : StringContent
  {
    public TrackingContent(string content, Encoding encoding, string mediaType)
        : base(content, encoding, mediaType)
    {
    }

    public bool IsDisposed { get; private set; }

    protected override void Dispose(bool disposing)
    {
      IsDisposed = true;
      base.Dispose(disposing);
    }
  }

  /// <summary>
  /// A read-only stream that yields a fixed byte prefix and then throws on the next read,
  /// simulating a connection dropped mid-stream.
  /// </summary>
  public sealed class ThrowingReadStream : Stream
  {
    private readonly byte[] _prefix;
    private readonly Exception _exception;
    private int _position;

    public ThrowingReadStream(byte[] prefix, Exception exception)
    {
      _prefix = prefix;
      _exception = exception;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
      get => _position;
      set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
      if (_position >= _prefix.Length)
        throw _exception;

      var available = Math.Min(count, _prefix.Length - _position);
      Array.Copy(_prefix, _position, buffer, offset, available);
      _position += available;
      return available;
    }

    public override ValueTask<int> ReadAsync(
        Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
      if (_position >= _prefix.Length)
        throw _exception;

      var available = Math.Min(buffer.Length, _prefix.Length - _position);
      _prefix.AsSpan(_position, available).CopyTo(buffer.Span);
      _position += available;
      return ValueTask.FromResult(available);
    }

    public override Task<int> ReadAsync(
        byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
  }
}
