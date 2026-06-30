using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers.Models.Connection;
using FluentDocker.Model.Models;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver
{
  /// <summary>
  /// Unit tests for <see cref="ModelApiConnection"/> via a loopback
  /// <see cref="HttpMessageHandler"/>; the unix-socket success path has its own unit test
  /// (<see cref="UnixSocket_LoopbackResponder_PingReturnsTrue"/>) backed by an in-process
  /// unix-domain-socket responder.
  /// </summary>
  [Trait("Category", "Unit")]
  public partial class ModelApiConnectionTests
  {
    private sealed class FuncHandler : HttpMessageHandler
    {
      private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
      public List<HttpRequestMessage> Requests { get; } = [];

      public FuncHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

      protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
      {
        Requests.Add(request);
        return Task.FromResult(_responder(request));
      }
    }

    private sealed class DelayHandler : HttpMessageHandler
    {
      private readonly TimeSpan _delay;
      private readonly Func<HttpResponseMessage> _factory;
      public DelayHandler(TimeSpan delay, Func<HttpResponseMessage> factory)
      {
        _delay = delay;
        _factory = factory;
      }

      protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
      {
        await Task.Delay(_delay, cancellationToken).ConfigureAwait(false);
        return _factory();
      }
    }

    private sealed class TrackingContent : HttpContent
    {
      private readonly byte[] _bytes;
      private readonly Action _onDispose;
      public TrackingContent(string text, Action onDispose)
      {
        _bytes = Encoding.UTF8.GetBytes(text);
        _onDispose = onDispose;
      }

      protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
          stream.WriteAsync(_bytes, 0, _bytes.Length);

      protected override bool TryComputeLength(out long length)
      {
        length = _bytes.Length;
        return true;
      }

      protected override void Dispose(bool disposing)
      {
        if (disposing)
          _onDispose();
        base.Dispose(disposing);
      }
    }

    private static HttpResponseMessage Json(HttpStatusCode code, string body) =>
        new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static ModelApiConnection Create(HttpMessageHandler handler) =>
        new(new Uri("http://localhost:12434"), handler);

    [Fact]
    public void BaseAddress_IsSet()
    {
      using var handler = new FuncHandler(_ => Json(HttpStatusCode.OK, "{}"));
      var conn = Create(handler);
      Assert.Equal(new Uri("http://localhost:12434"), conn.BaseAddress);
    }

    [Fact]
    public async Task Constructor_IPv6Endpoint_ProducesBracketedBaseAddress()
    {
      // An IPv6 literal must be bracketed in the rebuilt authority — otherwise the URI
      // (http://::1:12434) is invalid and construction would throw.
      await using var conn = new ModelApiConnection(
          ModelRunnerEndpoint.Custom(new Uri("http://[::1]:12434")));

      Assert.Equal("http://[::1]:12434/", conn.BaseAddress.ToString());
    }

    [Fact]
    public async Task UnixSocket_NonexistentPath_PingReturnsFalse()
    {
      // Exercises the unix-socket connect-failure path (the socket is disposed on a
      // failed connect); ping swallows the failure and reports unreachable.
      var socketPath = Path.Combine(Path.GetTempPath(), $"fd-no-such-{Guid.NewGuid():N}.sock");
      await using var conn = new ModelApiConnection(
          ModelRunnerEndpoint.UnixSocket(socketPath));

      Assert.False(await conn.PingAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UnixSocket_LoopbackResponder_PingReturnsTrue()
    {
      // M13: the unix-socket SUCCESS path. An in-process UDS responder accepts ONE connection
      // and writes a minimal "200 OK" HTTP/1.1 reply, so PingAsync drives the real
      // SocketsHttpHandler.ConnectCallback over a unix domain socket and reports reachable.
      if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        return; // Unix domain sockets are exercised on non-Windows platforms.

      // ponytail: bind under GetTempPath (like the repo's other UDS tests), not the repo's
      // .out/ — a bindable UDS path has a hard ~104-char sun_path kernel limit and the deep
      // bin/.out/ path overflows it. The socket file is deleted on responder dispose.
      var socketPath = Path.Combine(Path.GetTempPath(), $"fd-uds-{Guid.NewGuid():N}.sock");

      await using var responder = UnixSocketResponder.Start(socketPath);
      await using var conn = new ModelApiConnection(ModelRunnerEndpoint.UnixSocket(socketPath));

      Assert.True(await conn.PingAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetAsync_PassesPathThrough()
    {
      using var handler = new FuncHandler(_ => Json(HttpStatusCode.OK, "{\"ok\":true}"));
      var conn = Create(handler);

      using var resp = await conn.GetAsync("/engines/v1/models", TestContext.Current.CancellationToken);

      Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
      Assert.EndsWith("/engines/v1/models", handler.Requests[0].RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task PostAsync_SendsBody()
    {
      string? captured = null;
      using var handler = new FuncHandler(req =>
      {
        captured = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
        return Json(HttpStatusCode.OK, "{}");
      });
      var conn = Create(handler);

      using var content = new StringContent("{\"model\":\"ai/x\"}", Encoding.UTF8, "application/json");
      using var resp = await conn.PostAsync("/engines/v1/chat/completions", content, TestContext.Current.CancellationToken);

      Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
      Assert.Contains("ai/x", captured);
      Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
    }

    [Fact]
    public async Task DeleteAsync_UsesDeleteVerb()
    {
      using var handler = new FuncHandler(_ => Json(HttpStatusCode.OK, "{}"));
      var conn = Create(handler);

      using var resp = await conn.DeleteAsync("/models/ai/x", TestContext.Current.CancellationToken);

      Assert.Equal(HttpMethod.Delete, handler.Requests[0].Method);
    }

    [Fact]
    public async Task PostStreamAsync_ReturnsBodyStream()
    {
      const string sse = "data: {\"a\":1}\n\ndata: [DONE]\n\n";
      using var handler = new FuncHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
      {
        Content = new StringContent(sse, Encoding.UTF8, "text/event-stream")
      });
      var conn = Create(handler);

      using var content = new StringContent("{}", Encoding.UTF8, "application/json");
      await using var stream = await conn.PostStreamAsync("/engines/v1/chat/completions", content, TestContext.Current.CancellationToken);
      using var reader = new StreamReader(stream);
      var text = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);

      Assert.Contains("[DONE]", text);
    }

    [Fact]
    public async Task DisposeAsync_IsClean()
    {
      using var handler = new FuncHandler(_ => Json(HttpStatusCode.OK, "{}"));
      var conn = Create(handler);
      await conn.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_IsIdempotent()
    {
      // Disposing twice must not throw: _httpClient.Dispose() and X509Certificate2.Dispose()
      // are both no-ops on a second call.
      using var handler = new FuncHandler(_ => Json(HttpStatusCode.OK, "{}"));
      var conn = Create(handler);

      await conn.DisposeAsync();
      await conn.DisposeAsync();
    }

    [Fact]
    public async Task PostStreamAsync_DisposesResponseOnNonSuccess()
    {
      var disposed = false;
      using var handler = new FuncHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
      {
        Content = new TrackingContent("boom", () => disposed = true)
      });
      var conn = Create(handler);

      using var body = new StringContent("{}", Encoding.UTF8, "application/json");
      var ex = await Assert.ThrowsAsync<HttpRequestException>(() => conn.PostStreamAsync("/x", body, TestContext.Current.CancellationToken));

      // The failure must carry the status (so the inference driver can map 404 ->
      // ModelNotLoaded, 401 -> Unauthorized) and the (bounded) error body, and the
      // response/content must be disposed, not leaked.
      Assert.Equal(HttpStatusCode.InternalServerError, ex.StatusCode);
      Assert.Contains("boom", ex.Message, StringComparison.Ordinal);
      Assert.True(disposed, "the failed response/content must be disposed, not leaked");
    }

    [Fact]
    public async Task PostStreamAsync_NonSuccess_ThrowsWithStatusAndErrorBody()
    {
      using var handler = new FuncHandler(_ => Json(HttpStatusCode.NotFound, "{\"error\":\"no such model\"}"));
      var conn = Create(handler);

      using var body = new StringContent("{}", Encoding.UTF8, "application/json");
      var ex = await Assert.ThrowsAsync<HttpRequestException>(
          () => conn.PostStreamAsync("/x", body, TestContext.Current.CancellationToken));

      Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
      Assert.Contains("no such model", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostStreamAsync_NonSuccess_BoundsErrorBody()
    {
      var huge = new string('x', 5000);
      using var handler = new FuncHandler(_ => Json(HttpStatusCode.BadRequest, huge));
      var conn = Create(handler);

      using var body = new StringContent("{}", Encoding.UTF8, "application/json");
      var ex = await Assert.ThrowsAsync<HttpRequestException>(
          () => conn.PostStreamAsync("/x", body, TestContext.Current.CancellationToken));

      // A pathological error body must not be dumped verbatim into the exception.
      Assert.True(ex.Message.Length <= 512, $"error body must be bounded; was {ex.Message.Length}");
      Assert.EndsWith("…", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostStreamAsync_NonSuccess_BoundsErrorBodyRead_PastReadCap()
    {
      // The READ itself (not just the final string) must be bounded — a body far larger than
      // the 64 KiB read cap must not force unbounded buffering, yet still yield a capped message.
      var enormous = new string('y', 256 * 1024);
      using var handler = new FuncHandler(_ => Json(HttpStatusCode.BadRequest, enormous));
      var conn = Create(handler);

      using var body = new StringContent("{}", Encoding.UTF8, "application/json");
      var ex = await Assert.ThrowsAsync<HttpRequestException>(
          () => conn.PostStreamAsync("/x", body, TestContext.Current.CancellationToken));

      Assert.True(ex.Message.Length <= 512, $"error body must be bounded; was {ex.Message.Length}");
    }

    // A stream whose Dispose / DisposeAsync always throws — used to verify the owning stream
    // still disposes its HttpResponseMessage even when the inner stream's dispose faults.
    private sealed class ThrowingDisposeStream : Stream
    {
      public override bool CanRead => true;
      public override bool CanSeek => false;
      public override bool CanWrite => false;
      public override long Length => 0;
      public override long Position { get => 0; set { } }
      public override void Flush() { }
      public override int Read(byte[] buffer, int offset, int count) => 0;
      public override long Seek(long offset, SeekOrigin origin) => 0;
      public override void SetLength(long value) { }
      public override void Write(byte[] buffer, int offset, int count) { }

#pragma warning disable CA2215 // Intentional: simulates an inner stream whose dispose faults before reaching base.
      protected override void Dispose(bool disposing) => throw new IOException("inner dispose boom");

      public override ValueTask DisposeAsync() => throw new IOException("inner dispose boom async");
#pragma warning restore CA2215
    }

    // Records whether Dispose was called so the L1 tests can assert the response was disposed.
    private sealed class ProbeResponse : HttpResponseMessage
    {
      public bool Disposed { get; private set; }
      protected override void Dispose(bool disposing)
      {
        Disposed = true;
        base.Dispose(disposing);
      }
    }

    [Fact]
    public async Task ResponseOwningStream_DisposeAsync_InnerThrows_StillDisposesResponseAndPropagates()
    {
      var response = new ProbeResponse();
      var stream = new ResponseOwningStream(new ThrowingDisposeStream(), response);

      var ex = await Assert.ThrowsAsync<IOException>(async () => await stream.DisposeAsync());

      Assert.Equal("inner dispose boom async", ex.Message);
      Assert.True(response.Disposed, "the HttpResponseMessage must be disposed even when the inner stream's dispose throws");
    }

    [Fact]
    public void ResponseOwningStream_Dispose_InnerThrows_StillDisposesResponseAndPropagates()
    {
      var response = new ProbeResponse();
      var stream = new ResponseOwningStream(new ThrowingDisposeStream(), response);

      var ex = Assert.Throws<IOException>(() => stream.Dispose());

      Assert.Equal("inner dispose boom", ex.Message);
      Assert.True(response.Disposed, "the HttpResponseMessage must be disposed even when the inner stream's dispose throws");
    }

    /// <summary>
    /// A tiny in-process unix-domain-socket HTTP responder: binds a UDS at the given path,
    /// accepts ONE connection and writes a minimal empty-body <c>200 OK</c> reply. Used only to
    /// drive a real client-side connect over a unix socket; it is not a conformant HTTP server.
    /// </summary>
    private sealed class UnixSocketResponder : IAsyncDisposable
    {
      private readonly Socket _listener;
      private readonly string _path;
      private readonly CancellationTokenSource _cts = new();
      private readonly Task _loop;

      private UnixSocketResponder(Socket listener, string path)
      {
        _listener = listener;
        _path = path;
        _loop = Task.Run(AcceptOnceAsync);
      }

      public static UnixSocketResponder Start(string socketPath)
      {
        if (File.Exists(socketPath))
          File.Delete(socketPath);
        var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(socketPath));
        listener.Listen(1);
        return new UnixSocketResponder(listener, socketPath);
      }

      private async Task AcceptOnceAsync()
      {
        try
        {
          using var client = await _listener.AcceptAsync(_cts.Token).ConfigureAwait(false);
          var buffer = new byte[1024];
          await client.ReceiveAsync(buffer, SocketFlags.None, _cts.Token).ConfigureAwait(false);

          var response = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n");
          await client.SendAsync(response, SocketFlags.None, _cts.Token).ConfigureAwait(false);
        }
        catch
        {
          // Shutdown / cancellation / a client that hangs up early — best effort, the test only
          // needs ONE successful exchange to prove the unix-socket connect succeeded.
        }
      }

      public async ValueTask DisposeAsync()
      {
        await _cts.CancelAsync().ConfigureAwait(false);
        _listener.Dispose();
        try
        {
          await _loop.ConfigureAwait(false);
        }
        catch
        {
          // best-effort shutdown
        }
        _cts.Dispose();
        try
        {
          if (File.Exists(_path))
            File.Delete(_path);
        }
        catch
        {
          // best-effort cleanup of the socket file
        }
      }
    }
  }
}
