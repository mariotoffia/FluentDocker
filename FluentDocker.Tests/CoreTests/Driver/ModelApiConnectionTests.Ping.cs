using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers.Models.Connection;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver
{
  /// <summary>Reachability (ping) and transport-failure tests for <see cref="ModelApiConnection"/>.</summary>
  public partial class ModelApiConnectionTests
  {
    [Fact]
    public async Task PingAsync_TrueOnAnyHttpResponse()
    {
      // ANY HTTP response — including 4xx/5xx — proves the endpoint is reachable; only a
      // transport-level failure means unreachable. (Previously a 5xx false-negatived.)
      using var ok = new FuncHandler(_ => Json(HttpStatusCode.OK, "ok"));
      Assert.True(await Create(ok).PingAsync(TestContext.Current.CancellationToken));

      using var serverError = new FuncHandler(_ => Json(HttpStatusCode.InternalServerError, "no"));
      Assert.True(await Create(serverError).PingAsync(TestContext.Current.CancellationToken));

      using var notFound = new FuncHandler(_ => Json(HttpStatusCode.NotFound, "no"));
      Assert.True(await Create(notFound).PingAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PingAsync_FalseOnException()
    {
      using var handler = new FuncHandler(_ => throw new HttpRequestException("refused"));
      Assert.False(await Create(handler).PingAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PingAsync_404OnRoot_200OnModels_IsReachable()
    {
      // 404 for "/" but 200 for the model-list route: the endpoint is reachable and the probe
      // must target the model-list route, not "/".
      using var handler = new FuncHandler(req =>
          req.RequestUri!.AbsolutePath.EndsWith("/models", StringComparison.Ordinal)
              ? Json(HttpStatusCode.OK, "{}")
              : Json(HttpStatusCode.NotFound, "no"));

      // Base address carries the engine path so the derived probe path is /engines/v1/models.
      await using var conn = new ModelApiConnection(
          new Uri("http://localhost:12434/engines/v1"), handler);

      Assert.True(await conn.PingAsync(TestContext.Current.CancellationToken));
      Assert.NotEmpty(handler.Requests);
      Assert.EndsWith("/engines/v1/models", handler.Requests[0].RequestUri!.AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PingAsync_ConnectionRefused_IsNotReachable()
    {
      // A transport-level failure (connection refused) means unreachable.
      using var handler = new FuncHandler(_ => throw new HttpRequestException("Connection refused"));
      await using var conn = new ModelApiConnection(new Uri("http://localhost:12434/engines/v1"), handler);

      Assert.False(await conn.PingAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetAsync_ConnectionRefused_ThrowsEndpointUnreachable()
    {
      // A transport-level failure (HttpRequestException with no HTTP status) on a non-streaming
      // send must surface as a typed ModelRunnerException(EndpointUnreachable), not a generic fault.
      using var handler = new FuncHandler(_ => throw new HttpRequestException("Connection refused"));
      var conn = Create(handler);

      var ex = await Assert.ThrowsAsync<ModelRunnerException>(
          () => conn.GetAsync("/engines/v1/models", TestContext.Current.CancellationToken));
      Assert.Equal(ErrorCodes.ModelInference.EndpointUnreachable, ex.ErrorCode);
    }

    [Fact]
    public async Task PostAsync_ConnectionRefused_ThrowsEndpointUnreachable()
    {
      using var handler = new FuncHandler(_ => throw new SocketException((int)SocketError.ConnectionRefused));
      var conn = Create(handler);

      using var body = new StringContent("{}", Encoding.UTF8, "application/json");
      var ex = await Assert.ThrowsAsync<ModelRunnerException>(
          () => conn.PostAsync("/engines/v1/chat/completions", body, TestContext.Current.CancellationToken));
      Assert.Equal(ErrorCodes.ModelInference.EndpointUnreachable, ex.ErrorCode);
    }

    [Fact]
    public async Task GetAsync_HttpErrorResponse_DoesNotThrow()
    {
      // A genuine HTTP error RESPONSE (5xx with a body) is NOT a transport failure — the verb
      // methods return it as a response, and the caller (inference driver) maps it to RequestFailed.
      using var handler = new FuncHandler(_ => Json(HttpStatusCode.InternalServerError, "{\"error\":\"boom\"}"));
      var conn = Create(handler);

      using var resp = await conn.GetAsync("/engines/v1/models", TestContext.Current.CancellationToken);
      Assert.Equal(HttpStatusCode.InternalServerError, resp.StatusCode);
    }

    [Fact]
    public async Task PostStreamAsync_ConnectionRefused_ThrowsEndpointUnreachable()
    {
      // A transport failure opening the stream maps to EndpointUnreachable; an HTTP error STATUS
      // is delivered as a response and continues to surface as HttpRequestException (asserted
      // elsewhere) so the streaming driver can map 404/401.
      using var handler = new FuncHandler(_ => throw new HttpRequestException("Connection refused"));
      var conn = Create(handler);

      using var body = new StringContent("{}", Encoding.UTF8, "application/json");
      var ex = await Assert.ThrowsAsync<ModelRunnerException>(
          () => conn.PostStreamAsync("/engines/v1/chat/completions", body, TestContext.Current.CancellationToken));
      Assert.Equal(ErrorCodes.ModelInference.EndpointUnreachable, ex.ErrorCode);
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
      using var reader = new System.IO.StreamReader(stream);
      Assert.Contains("[DONE]", await reader.ReadToEndAsync(TestContext.Current.CancellationToken));
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
