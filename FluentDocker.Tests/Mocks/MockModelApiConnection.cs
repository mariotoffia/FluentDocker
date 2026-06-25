using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers.Models.Connection;

namespace FluentDocker.Tests.Mocks
{
  /// <summary>A request captured by <see cref="MockModelApiConnection"/>.</summary>
  /// <param name="Method">The HTTP method.</param>
  /// <param name="Path">The request path.</param>
  /// <param name="Body">The request body (when applicable).</param>
  public sealed record CapturedModelRequest(string Method, string Path, string Body);

  /// <summary>
  /// A hand-rolled (no Moq) <see cref="IModelApiConnection"/> with programmable
  /// JSON responses, SSE stream scripts and forced / mid-stream faults — the
  /// inference-side counterpart to <c>MockDockerApiConnection</c>.
  /// </summary>
  public sealed class MockModelApiConnection : IModelApiConnection
  {
    private readonly record struct ResponseEntry(
        string Method, string PathContains, HttpStatusCode StatusCode,
        string JsonBody, string StreamContent, byte[] StreamBytes, int FaultAfterBytes, byte[][] StreamChunks = null);

    private readonly List<ResponseEntry> _entries = [];
    private readonly List<CapturedModelRequest> _requests = [];
    private bool _pingSuccess = true;

    /// <inheritdoc />
    public Uri BaseAddress { get; set; } = new("http://localhost:12434");

    /// <inheritdoc />
    public TimeSpan? StreamReadIdleTimeout { get; set; }

    /// <summary>Registers a canned GET response.</summary>
    public MockModelApiConnection SetupGet(string pathContains, int statusCode, string jsonBody)
    {
      _entries.Add(new ResponseEntry("GET", pathContains, (HttpStatusCode)statusCode, jsonBody, null, null, -1));
      return this;
    }

    /// <summary>Registers a canned POST response.</summary>
    public MockModelApiConnection SetupPost(string pathContains, int statusCode, string jsonBody)
    {
      _entries.Add(new ResponseEntry("POST", pathContains, (HttpStatusCode)statusCode, jsonBody, null, null, -1));
      return this;
    }

    /// <summary>Registers a canned DELETE response.</summary>
    public MockModelApiConnection SetupDelete(string pathContains, int statusCode, string jsonBody)
    {
      _entries.Add(new ResponseEntry("DELETE", pathContains, (HttpStatusCode)statusCode, jsonBody, null, null, -1));
      return this;
    }

    /// <summary>Registers a canned text stream (e.g. an SSE script) for POST-stream.</summary>
    public MockModelApiConnection SetupStream(string pathContains, string streamContent)
    {
      _entries.Add(new ResponseEntry("STREAM", pathContains, HttpStatusCode.OK, null, streamContent, null, -1));
      return this;
    }

    /// <summary>
    /// Registers a non-success status for POST-stream. Mirrors the real
    /// <c>ModelApiConnection.PostStreamAsync</c>, which on a non-2xx response reads a
    /// bounded error body and throws <see cref="HttpRequestException"/> carrying the
    /// status code (the inference driver maps it to a typed <c>ModelRunnerException</c>).
    /// </summary>
    public MockModelApiConnection SetupStreamStatus(string pathContains, int statusCode, string errorBody = null)
    {
      _entries.Add(new ResponseEntry("STREAM", pathContains, (HttpStatusCode)statusCode, null, errorBody, null, -1));
      return this;
    }

    /// <summary>Registers a canned raw-byte stream for POST-stream.</summary>
    public MockModelApiConnection SetupStreamBytes(string pathContains, byte[] bytes)
    {
      _entries.Add(new ResponseEntry("STREAM", pathContains, HttpStatusCode.OK, null, null, bytes, -1));
      return this;
    }

    /// <summary>
    /// Registers a stream whose bytes are delivered in the given pre-set slices — one slice per
    /// underlying read — so a single SSE frame can be split across a read boundary. Used to verify
    /// that a partial <c>data:</c> frame is reassembled before being yielded.
    /// </summary>
    public MockModelApiConnection SetupStreamChunks(string pathContains, params string[] chunks)
    {
      var slices = new byte[chunks.Length][];
      for (var i = 0; i < chunks.Length; i++)
        slices[i] = Encoding.UTF8.GetBytes(chunks[i] ?? string.Empty);
      _entries.Add(new ResponseEntry("STREAM", pathContains, HttpStatusCode.OK, null, null, null, -1, slices));
      return this;
    }

    /// <summary>
    /// Registers a stream that yields <paramref name="streamContent"/> then throws an
    /// <see cref="IOException"/> after <paramref name="faultAfterBytes"/> bytes (mid-stream fault).
    /// </summary>
    public MockModelApiConnection SetupStreamFault(string pathContains, string streamContent, int faultAfterBytes)
    {
      _entries.Add(new ResponseEntry("STREAM", pathContains, HttpStatusCode.OK, null, streamContent, null, faultAfterBytes));
      return this;
    }

    /// <summary>
    /// Registers a stream that delivers <paramref name="prefixContent"/> (if any) and then
    /// blocks on every subsequent read until the caller's <see cref="CancellationToken"/> is
    /// cancelled — used to verify that idle-timeout and cancellation paths both fire correctly.
    /// </summary>
    public MockModelApiConnection SetupStreamStalling(string pathContains, string prefixContent = null)
    {
      _entries.Add(new ResponseEntry("STREAM_STALL", pathContains, HttpStatusCode.OK, null, prefixContent, null, -1));
      return this;
    }

    /// <summary>Sets the ping result.</summary>
    public MockModelApiConnection SetupPing(bool success)
    {
      _pingSuccess = success;
      return this;
    }

    /// <summary>The requests captured so far.</summary>
    public IReadOnlyList<CapturedModelRequest> GetRequests() => _requests;

    /// <inheritdoc />
    public Task<HttpResponseMessage> GetAsync(string path, CancellationToken ct = default)
    {
      Record("GET", path, null);
      return Task.FromResult(Resolve("GET", path));
    }

    /// <inheritdoc />
    public async Task<HttpResponseMessage> PostAsync(string path, HttpContent content, CancellationToken ct = default)
    {
      var body = content is not null ? await content.ReadAsStringAsync(ct) : null;
      Record("POST", path, body);
      return Resolve("POST", path);
    }

    /// <inheritdoc />
    public Task<HttpResponseMessage> DeleteAsync(string path, CancellationToken ct = default)
    {
      Record("DELETE", path, null);
      return Task.FromResult(Resolve("DELETE", path));
    }

    /// <inheritdoc />
    public async Task<Stream> PostStreamAsync(string path, HttpContent content, CancellationToken ct = default)
    {
      var body = content is not null ? await content.ReadAsStringAsync(ct) : null;
      Record("POST_STREAM", path, body);
      return ResolveStream(path);
    }

    /// <inheritdoc />
    public Task<bool> PingAsync(CancellationToken ct = default)
    {
      Record("PING", "/_ping", null);
      return Task.FromResult(_pingSuccess);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private void Record(string method, string path, string body) =>
        _requests.Add(new CapturedModelRequest(method, path, body));

    // Route by path SUFFIX (after stripping any query), not a loose Contains — a
    // registered "/models" must not silently satisfy a request for the wrong endpoint
    // (e.g. "/models/ai/x"), which would mask a path bug in the code under test.
    private static bool MatchesPath(string actual, string registered)
    {
      var q = actual.IndexOf('?');
      var p = q >= 0 ? actual[..q] : actual;
      return p.EndsWith(registered, StringComparison.Ordinal);
    }

    private HttpResponseMessage Resolve(string method, string path)
    {
      var match = _entries.Where(e => e.Method == method && MatchesPath(path, e.PathContains))
          .Select(e => (ResponseEntry?)e).LastOrDefault();

      if (match is null)
        return new HttpResponseMessage(HttpStatusCode.NotFound)
        {
          Content = new StringContent($"{{\"message\":\"no mock for {method} {path}\"}}", Encoding.UTF8, "application/json")
        };

      return new HttpResponseMessage(match.Value.StatusCode)
      {
        Content = new StringContent(match.Value.JsonBody ?? "{}", Encoding.UTF8, "application/json")
      };
    }

    private Stream ResolveStream(string path)
    {
      // Match STREAM_STALL first (higher specificity), then fall back to STREAM.
      var entry = _entries.Where(e => (e.Method == "STREAM_STALL" || e.Method == "STREAM") && MatchesPath(path, e.PathContains))
          .Select(e => (ResponseEntry?)e).LastOrDefault()
          ?? throw new InvalidOperationException($"no mock stream for {path}");

      if (entry.Method == "STREAM_STALL")
      {
        var prefix = entry.StreamContent is null
            ? Array.Empty<byte>()
            : Encoding.UTF8.GetBytes(entry.StreamContent);
        return new StallingStream(prefix);
      }

      // Mirror the real connection: a non-2xx response never yields a stream — it
      // throws HttpRequestException (with the status code) before any body is read.
      var status = (int)entry.StatusCode;
      if (status is < 200 or > 299)
        throw new HttpRequestException(
            string.IsNullOrEmpty(entry.StreamContent) ? $"HTTP {status}" : entry.StreamContent,
            null, entry.StatusCode);

      if (entry.StreamChunks is not null)
        return new ChunkedStream(entry.StreamChunks);

      var bytes = entry.StreamBytes ?? Encoding.UTF8.GetBytes(entry.StreamContent ?? string.Empty);
      if (entry.FaultAfterBytes >= 0)
        return new FaultingStream(bytes, entry.FaultAfterBytes);

      return new MemoryStream(bytes);
    }

    /// <summary>A stream that yields a prefix then throws to simulate a mid-stream fault.</summary>
    private sealed class FaultingStream : Stream
    {
      private readonly byte[] _data;
      private readonly int _faultAfter;
      private int _position;

      public FaultingStream(byte[] data, int faultAfter)
      {
        _data = data;
        _faultAfter = faultAfter;
      }

      public override bool CanRead => true;
      public override bool CanSeek => false;
      public override bool CanWrite => false;
      public override long Length => _data.Length;
      public override long Position { get => _position; set => throw new NotSupportedException(); }

      public override int Read(byte[] buffer, int offset, int count)
      {
        if (_position >= _faultAfter)
          throw new IOException("Simulated mid-stream fault");

        var available = Math.Min(count, _faultAfter - _position);
        Array.Copy(_data, _position, buffer, offset, available);
        _position += available;
        return available;
      }

      public override void Flush()
      {
      }

      public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
      public override void SetLength(long value) => throw new NotSupportedException();
      public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>
    /// Yields <see cref="_prefix"/> bytes and then blocks every subsequent read until the
    /// caller's <see cref="CancellationToken"/> fires — simulates a server that stops
    /// sending after a partial response (used to exercise idle-timeout / cancellation paths).
    /// </summary>
    private sealed class StallingStream : Stream
    {
      private readonly byte[] _prefix;
      private int _position;

      public StallingStream(byte[] prefix) => _prefix = prefix;

      public override bool CanRead => true;
      public override bool CanSeek => false;
      public override bool CanWrite => false;
      public override long Length => _prefix.Length;
      public override long Position { get => _position; set => throw new NotSupportedException(); }

      public override int Read(byte[] buffer, int offset, int count)
      {
        if (_position < _prefix.Length)
        {
          var n = Math.Min(count, _prefix.Length - _position);
          Array.Copy(_prefix, _position, buffer, offset, n);
          _position += n;
          return n;
        }
        // Stall forever (synchronous read blocks; tests should use async).
        Thread.Sleep(Timeout.Infinite);
        return 0;
      }

      public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
      {
        if (_position < _prefix.Length)
        {
          var n = Math.Min(buffer.Length, _prefix.Length - _position);
          _prefix.AsMemory(_position, n).CopyTo(buffer);
          _position += n;
          return n;
        }
        // Stall until the token is cancelled.
        await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
        return 0;
      }

      public override void Flush() { }
      public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
      public override void SetLength(long value) => throw new NotSupportedException();
      public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>
    /// Delivers its content as a fixed sequence of byte slices, returning AT MOST ONE pending
    /// slice per read. This lets a test split a single SSE frame across two reads to verify the
    /// driver reassembles a partial <c>data:</c> frame before yielding it.
    /// </summary>
    private sealed class ChunkedStream : Stream
    {
      private readonly byte[][] _chunks;
      private int _chunkIndex;
      private int _offsetInChunk;

      public ChunkedStream(byte[][] chunks) => _chunks = chunks;

      public override bool CanRead => true;
      public override bool CanSeek => false;
      public override bool CanWrite => false;
      public override long Length => throw new NotSupportedException();
      public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

      private int ReadCore(Span<byte> buffer)
      {
        // Skip any fully-consumed (or empty) leading slices.
        while (_chunkIndex < _chunks.Length && _offsetInChunk >= _chunks[_chunkIndex].Length)
        {
          _chunkIndex++;
          _offsetInChunk = 0;
        }

        if (_chunkIndex >= _chunks.Length)
          return 0;

        var chunk = _chunks[_chunkIndex];
        var n = Math.Min(buffer.Length, chunk.Length - _offsetInChunk);
        chunk.AsSpan(_offsetInChunk, n).CopyTo(buffer);
        _offsetInChunk += n;
        return n;
      }

      public override int Read(byte[] buffer, int offset, int count) => ReadCore(buffer.AsSpan(offset, count));

      public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) =>
          new(ReadCore(buffer.Span));

      public override void Flush() { }
      public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
      public override void SetLength(long value) => throw new NotSupportedException();
      public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
  }
}
