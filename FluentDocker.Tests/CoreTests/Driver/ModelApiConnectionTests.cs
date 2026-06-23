using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers.Models.Connection;
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

    private static HttpResponseMessage Json(HttpStatusCode code, string body) =>
        new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static ModelApiConnection Create(FuncHandler handler) =>
        new(new Uri("http://localhost:12434"), handler);

    [Fact]
    public void BaseAddress_IsSet()
    {
      using var handler = new FuncHandler(_ => Json(HttpStatusCode.OK, "{}"));
      var conn = Create(handler);
      Assert.Equal(new Uri("http://localhost:12434"), conn.BaseAddress);
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
  }
}
