using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
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
  /// <see cref="HttpMessageHandler"/> (the unix-socket path is exercised by
  /// integration tests).
  /// </summary>
  [Trait("Category", "Unit")]
  public class ModelApiConnectionTests
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

      protected override Task SerializeToStreamAsync(Stream stream, TransportContext context) =>
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
          FluentDocker.Model.Models.ModelRunnerEndpoint.Custom(new Uri("http://[::1]:12434")));

      Assert.Equal("http://[::1]:12434/", conn.BaseAddress.ToString());
    }

    [Fact]
    public async Task UnixSocket_NonexistentPath_PingReturnsFalse()
    {
      // Exercises the unix-socket connect-failure path (the socket is disposed on a
      // failed connect); ping swallows the failure and reports unreachable.
      var socketPath = Path.Combine(Path.GetTempPath(), $"fd-no-such-{Guid.NewGuid():N}.sock");
      await using var conn = new ModelApiConnection(
          FluentDocker.Model.Models.ModelRunnerEndpoint.UnixSocket(socketPath));

      Assert.False(await conn.PingAsync(TestContext.Current.CancellationToken));
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
      string captured = null;
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
    public async Task PingAsync_TrueOnSuccess_FalseOnError()
    {
      using var ok = new FuncHandler(_ => Json(HttpStatusCode.OK, "ok"));
      Assert.True(await Create(ok).PingAsync(TestContext.Current.CancellationToken));

      using var bad = new FuncHandler(_ => Json(HttpStatusCode.InternalServerError, "no"));
      Assert.False(await Create(bad).PingAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PingAsync_FalseOnException()
    {
      using var handler = new FuncHandler(_ => throw new HttpRequestException("refused"));
      Assert.False(await Create(handler).PingAsync(TestContext.Current.CancellationToken));
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
    public async Task DisposeAsync_WithClientCertificate_DisposesCleanly()
    {
      // Exercises the TLS path that creates a client X509Certificate2 (cert.pem + key.pem):
      // construction succeeds, and disposing the connection (which now owns the cert) does
      // not throw and is idempotent.
      var dir = WritePemCertificates(includeCa: false);
      try
      {
        var config = new ModelApiConnectionConfig
        {
          CertificatePath = dir,
          // Avoid needing a server handshake; we only assert construction + dispose are clean.
          VerifyTls = false
        };

        var conn = new ModelApiConnection(
            ModelRunnerEndpoint.Custom(new Uri("https://localhost:12434")), config);

        await conn.DisposeAsync();
        await conn.DisposeAsync();
      }
      finally
      {
        Directory.Delete(dir, recursive: true);
      }
    }

    [Fact]
    public async Task DisposeAsync_WithClientAndCaCertificate_DisposesCleanly()
    {
      // Exercises both cert-creating branches: the client cert (cert.pem + key.pem) and the
      // custom CA (ca.pem) captured by the TLS validation callback. Both must be owned by the
      // connection and disposed cleanly (and idempotently) without throwing.
      var dir = WritePemCertificates(includeCa: true);
      try
      {
        var config = new ModelApiConnectionConfig
        {
          CertificatePath = dir,
          VerifyTls = true
        };

        var conn = new ModelApiConnection(
            ModelRunnerEndpoint.Custom(new Uri("https://localhost:12434")), config);

        await conn.DisposeAsync();
        await conn.DisposeAsync();
      }
      finally
      {
        Directory.Delete(dir, recursive: true);
      }
    }

    /// <summary>
    /// Writes a self-signed certificate as <c>cert.pem</c>/<c>key.pem</c> (and optionally a
    /// <c>ca.pem</c>) into a fresh temp directory and returns that directory's path.
    /// </summary>
    private static string WritePemCertificates(bool includeCa)
    {
      var dir = Path.Combine(Path.GetTempPath(), $"fd-modelconn-certs-{Guid.NewGuid():N}");
      Directory.CreateDirectory(dir);

      using var rsa = RSA.Create(2048);
      var request = new CertificateRequest(
          "CN=fluentdocker-test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
      using var cert = request.CreateSelfSigned(
          DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));

      File.WriteAllText(Path.Combine(dir, "cert.pem"), cert.ExportCertificatePem());
      File.WriteAllText(Path.Combine(dir, "key.pem"), rsa.ExportPkcs8PrivateKeyPem());

      if (includeCa)
        File.WriteAllText(Path.Combine(dir, "ca.pem"), cert.ExportCertificatePem());

      return dir;
    }

    [Fact]
    public async Task GetAsync_NonStreaming_TimesOutPerRequestTimeout()
    {
      using var handler = new DelayHandler(TimeSpan.FromSeconds(5), () => Json(HttpStatusCode.OK, "{}"));
      var conn = new ModelApiConnection(new Uri("http://localhost:12434"), handler,
          requestTimeout: TimeSpan.FromMilliseconds(100));

      await Assert.ThrowsAsync<TimeoutException>(() => conn.GetAsync("/x", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PostStreamAsync_NotSubjectToRequestTimeout()
    {
      // Delay exceeds the (tiny) request timeout, yet the stream call must still succeed —
      // streaming is intentionally exempt from the non-streaming request timeout.
      using var handler = new DelayHandler(TimeSpan.FromMilliseconds(150),
          () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("data: [DONE]\n\n") });
      var conn = new ModelApiConnection(new Uri("http://localhost:12434"), handler,
          requestTimeout: TimeSpan.FromMilliseconds(30));

      using var body = new StringContent("{}", Encoding.UTF8, "application/json");
      await using var stream = await conn.PostStreamAsync("/x", body, TestContext.Current.CancellationToken);
      using var reader = new StreamReader(stream);
      Assert.Contains("[DONE]", await reader.ReadToEndAsync(TestContext.Current.CancellationToken));
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
    }

    [Fact]
    public async Task PingAsync_RethrowsOnCallerCancellation()
    {
      using var handler = new DelayHandler(TimeSpan.FromSeconds(5), () => Json(HttpStatusCode.OK, "ok"));
      var conn = Create(handler);
      using var cts = new CancellationTokenSource();
      cts.Cancel();

      await Assert.ThrowsAnyAsync<OperationCanceledException>(() => conn.PingAsync(cts.Token));
    }

    [Fact]
    public async Task PingAsync_ExceedingRequestTimeout_ReturnsFalse()
    {
      // A hung endpoint must not make ping block forever (HttpClient.Timeout is infinite).
      // The request timeout bounds it, and a ping timeout reports unreachable (false).
      using var handler = new DelayHandler(TimeSpan.FromSeconds(5), () => Json(HttpStatusCode.OK, "{}"));
      var conn = new ModelApiConnection(new Uri("http://localhost:12434"), handler,
          requestTimeout: TimeSpan.FromMilliseconds(100));

      Assert.False(await conn.PingAsync(TestContext.Current.CancellationToken));
    }
  }
}
