using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using FluentDocker.Drivers.Docker.Api.Connection;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.DockerApi
{
  public sealed partial class DockerApiChunk2RemediationTests
  {
    [Fact]
    public async Task PostStreamAsync_WithRequestContent_DoesNotApplyHeaderTimeout()
    {
      await using var server = await LoopbackHttpServer.StartAsync(async req =>
      {
        if (req.Path == "/v1.45/upload")
          await Task.Delay(250, TestContext.Current.CancellationToken);
        return LoopbackHttpServer.Ok("ok");
      });
      await using var conn = new DockerApiConnection(new DockerApiConnectionConfig
      {
        Host = server.BaseAddress,
        ApiVersion = "1.45",
        ConnectionTimeout = TimeSpan.FromMilliseconds(100),
        RequestTimeout = TimeSpan.FromSeconds(5)
      });

      await using var stream = await conn.PostStreamAsync(
          "/upload", new StringContent("payload"), TestContext.Current.CancellationToken)
          ;

      using var reader = new StreamReader(stream);
      Assert.Equal("ok", await reader.ReadToEndAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task NegotiateApiVersion_ClampsDaemonMaxToClientMax()
    {
      await using var server = await LoopbackHttpServer.StartAsync(req =>
      {
        if (req.Path == "/_ping")
          return Task.FromResult(LoopbackHttpServer.Ok("OK", ("API-Version", "9.99")));
        return Task.FromResult(LoopbackHttpServer.Ok("{}"));
      });
      await using var conn = new DockerApiConnection(new DockerApiConnectionConfig
      {
        Host = server.BaseAddress,
        ConnectionTimeout = TimeSpan.FromSeconds(2),
        RequestTimeout = TimeSpan.FromSeconds(2)
      });

      using var response = await conn.GetAsync("/version", TestContext.Current.CancellationToken)
          ;

      Assert.Equal("1.45", conn.ApiVersion);
      Assert.Contains("/v1.45/version", server.Paths);
    }

    [Fact]
    public async Task NegotiateApiVersion_DaemonBelowFloor_FailsClearly()
    {
      await using var server = await LoopbackHttpServer.StartAsync(req =>
      {
        if (req.Path == "/_ping")
          return Task.FromResult(LoopbackHttpServer.Ok("OK"));
        if (req.Path == "/version")
          return Task.FromResult(LoopbackHttpServer.Ok(
              @"{""ApiVersion"":""1.20"",""MinAPIVersion"":""1.12""}"));
        return Task.FromResult(LoopbackHttpServer.Ok("{}"));
      });
      await using var conn = new DockerApiConnection(new DockerApiConnectionConfig
      {
        Host = server.BaseAddress,
        ConnectionTimeout = TimeSpan.FromSeconds(2),
        RequestTimeout = TimeSpan.FromSeconds(2)
      });

      var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
          conn.GetAsync("/info", TestContext.Current.CancellationToken));

      Assert.Contains("daemon API version 1.20 is too old", ex.Message);
      Assert.Contains("need >= 1.24", ex.Message);
    }

  }
}
