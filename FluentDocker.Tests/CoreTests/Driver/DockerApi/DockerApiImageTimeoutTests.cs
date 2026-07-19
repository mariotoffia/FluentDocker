using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Api.Components;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.DockerApi
{
  [Trait("Category", "Unit")]
  public class DockerApiImageTimeoutTests
  {
    private static DriverContext Ctx => new("docker-api-image-timeout-test");

    private static DockerApiImageDriver CreateDriver(MockDockerApiConnection conn)
    {
      var driver = new DockerApiImageDriver(conn);
      driver.Initialize(Ctx);
      return driver;
    }

    [Fact]
    public async Task PullAsync_InternalTimeout_ReturnsCommandFailure()
    {
      var conn = new MockDockerApiConnection();
      conn.SetupStreamThrows("/images/create", new TaskCanceledException("request timed out"));
      var driver = CreateDriver(conn);

      var result = await driver.PullAsync(Ctx, "nginx", "latest", null!, CancellationToken.None);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Image.PullFailed, result.ErrorCode);
      Assert.Contains("Cannot connect to Docker daemon", result.Error);
    }

    [Fact]
    public async Task PushAsync_InternalTimeout_ReturnsCommandFailure()
    {
      var conn = new MockDockerApiConnection();
      conn.SetupStreamThrows("/push", new TaskCanceledException("request timed out"));
      var driver = CreateDriver(conn);

      var result = await driver.PushAsync(Ctx, "repo/app:latest", null!, CancellationToken.None);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Image.PushFailed, result.ErrorCode);
      Assert.Contains("Cannot connect to Docker daemon", result.Error);
    }
  }
}
