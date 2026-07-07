using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Cli.Binary;
using FluentDocker.Drivers.Docker.Cli.Components;
using FluentDocker.Model.Common;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Docker
{
  [Trait("Category", "Unit")]
  public class DockerCliLogFollowTests
  {
    [Fact]
    public async Task ComposeGetLogsAsync_WithFollow_ReturnsFailureWithoutExecuting()
    {
      var driver = new DockerCliComposeDriver(new TestBinaryResolver());

      var response = await driver.GetLogsAsync(
          new DriverContext("docker"),
          new ComposeLogsConfig { Follow = true },
          TestContext.Current.CancellationToken);

      Assert.False(response.Success);
      Assert.Contains("follow=true", response.Error);
    }

    [Fact]
    public async Task ServiceGetLogsAsync_WithFollow_ReturnsFailureWithoutExecuting()
    {
      var driver = new DockerCliServiceDriver(new TestBinaryResolver());

      var response = await driver.GetLogsAsync(
          new DriverContext("docker"),
          "svc",
          new ServiceLogsConfig { Follow = true },
          TestContext.Current.CancellationToken);

      Assert.False(response.Success);
      Assert.Contains("follow=true", response.Error);
    }

    private sealed class TestBinaryResolver : IBinaryResolver
    {
      private readonly DockerBinary _docker = new(".", "docker", SudoMechanism.None, string.Empty, DockerBinaryType.DockerClient);

      public DockerBinary[] Binaries => [_docker];
      public DockerBinary MainDockerClient => _docker;
      public DockerBinary MainDockerCli => _docker;
      public DockerBinary Resolve(string binary) => _docker;
      public string ResolveBinaryPath(string dockerCommand) => "docker";
    }
  }
}
