using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers.Docker.Api.Connection;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.DockerApi
{
  /// <summary>
  /// Tests for DockerApiConnection timeout and error handling.
  /// </summary>
  [Trait("Category", "Unit")]
  public class DockerApiConnectionTimeoutTests
  {
    [Fact]
    public async Task GetAsync_WhenConnectionRefused_ThrowsHttpRequestException()
    {
      var config = new DockerApiConnectionConfig
      {
        Host = "tcp://localhost:1",
        ApiVersion = "1.45",
        ConnectionTimeout = TimeSpan.FromSeconds(1),
        RequestTimeout = TimeSpan.FromSeconds(2)
      };

      await using var conn = new DockerApiConnection(config);

      await Assert.ThrowsAsync<HttpRequestException>(
          () => conn.GetAsync("/containers/json", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PostAsync_WhenConnectionRefused_ThrowsHttpRequestException()
    {
      var config = new DockerApiConnectionConfig
      {
        Host = "tcp://localhost:1",
        ApiVersion = "1.45",
        ConnectionTimeout = TimeSpan.FromSeconds(1),
        RequestTimeout = TimeSpan.FromSeconds(2)
      };

      await using var conn = new DockerApiConnection(config);

      await Assert.ThrowsAsync<HttpRequestException>(
          () => conn.PostAsync("/containers/create", null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task NegotiateApiVersion_WhenPingFails_ProceedsWithoutVersion()
    {
      // Connection to unreachable port - negotiation will fail
      var config = new DockerApiConnectionConfig
      {
        Host = "tcp://localhost:1",
        ConnectionTimeout = TimeSpan.FromSeconds(1),
        RequestTimeout = TimeSpan.FromSeconds(2)
        // No ApiVersion set - will trigger negotiation
      };

      await using var conn = new DockerApiConnection(config);
      Assert.Null(conn.ApiVersion);
      Assert.False(conn.IsVersionNegotiated);

      // PingAsync should fail gracefully
      var result = await conn.PingAsync(TestContext.Current.CancellationToken);
      Assert.False(result);
    }

    [Fact]
    public async Task GetAsync_WhenCancelled_ThrowsOperationCanceledException()
    {
      var config = new DockerApiConnectionConfig
      {
        Host = "tcp://localhost:1",
        ApiVersion = "1.45",
        ConnectionTimeout = TimeSpan.FromSeconds(30),
        RequestTimeout = TimeSpan.FromSeconds(30)
      };

      await using var conn = new DockerApiConnection(config);
      using var cts = new CancellationTokenSource();
      cts.Cancel(); // Cancel immediately

      await Assert.ThrowsAnyAsync<OperationCanceledException>(
          () => conn.GetAsync("/containers/json", cts.Token));
    }
  }
}
