using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Cli.Components;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Docker
{
  public partial class DockerCliComposeArgsTests
  {
    [Fact]
    public void BuildListSubArgs_Quiet_DoesNotEmitQuietFlag()
    {
      var result = DockerCliComposeDriver.BuildListSubArgs(new ComposeListConfig { Quiet = true });

      Assert.Equal("ps --format json", result);
      Assert.DoesNotContain(" -q", result);
    }
  }
}
