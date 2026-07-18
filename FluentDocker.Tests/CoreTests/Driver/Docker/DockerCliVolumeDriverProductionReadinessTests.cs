using System;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Cli.Components;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Docker
{
  /// <summary>
  /// Production-readiness coverage for <see cref="DockerCliVolumeDriver"/>: surfacing the
  /// dedicated list-failed error code when the CLI fails.
  /// </summary>
  [Trait("Category", "Unit")]
  [Trait("Requires", "PosixShell")]
  public sealed class DockerCliVolumeDriverProductionReadinessTests : DockerCliFakeDockerTestBase
  {
    [Fact]
    public async Task VolumeList_UsesVolumeListFailedErrorCode()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var driver = new DockerCliVolumeDriver(new FakeResolver(CreateFakeDocker("""
#!/bin/sh
echo 'boom' 1>&2
exit 1
""")));
      driver.Initialize(new DriverContext("docker"));

      var result = await driver.ListAsync(new DriverContext("docker"), cancellationToken: TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Volume.ListFailed, result.ErrorCode);
    }
  }
}
