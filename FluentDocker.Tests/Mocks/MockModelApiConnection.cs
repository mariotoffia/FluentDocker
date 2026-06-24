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
        string JsonBody, string StreamContent, byte[] StreamBytes, int FaultAfterBytes);

    private readonly List<ResponseEntry> _entries = [];
    private readonly List<CapturedModelRequest> _requests = [];
    private bool _pingSuccess = true;

    /// <inheritdoc />
    public Uri BaseAddress { get; set; } = new("http://localhost:12434");

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

    /// <summary>Registers a canned raw-byte stream for POST-stream.</summary>
    public MockModelApiConnection SetupStreamBytes(string pathContains, byte[] bytes)
    {
      _entries.Add(new ResponseEntry("STREAM", pathContains, HttpStatusCode.OK, null, null, bytes, -1));
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
      var entry = _entries.Where(e => e.Method == "STREAM" && MatchesPath(path, e.PathContains))
          .Select(e => (ResponseEntry?)e).LastOrDefault()
          ?? throw new InvalidOperationException($"no mock stream for {path}");
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
  }
}
