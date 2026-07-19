using System;
using System.IO;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Cli.Components;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Docker
{
  /// <summary>
  /// Production-readiness coverage for <see cref="DockerCliNetworkDriver"/>: mapping an empty
  /// inspect result to a not-found error, and emitting the <c>--ip-range</c> flag on create.
  /// </summary>
  [Trait("Category", "Unit")]
  [Trait("Requires", "PosixShell")]
  public sealed class DockerCliNetworkDriverProductionReadinessTests : DockerCliFakeDockerTestBase
  {
    [Fact]
    public async Task NetworkInspect_EmptyResultFailsAsNotFound()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var driver = new DockerCliNetworkDriver(new FakeResolver(CreateRecordingDocker(
          Path.Combine(TestOutputDirectory(), $"network-{Guid.NewGuid():N}.txt"),
          "[]")));
      driver.Initialize(new DriverContext("docker"));

      var result = await driver.InspectAsync(new DriverContext("docker"), "missing-net", TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Network.NotFound, result.ErrorCode);
    }

    [Fact]
    public async Task NetworkCreate_WithIpRange_UsesIpRangeFlag()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var record = Path.Combine(TestOutputDirectory(), $"args-{Guid.NewGuid():N}.txt");
      var driver = new DockerCliNetworkDriver(new FakeResolver(CreateRecordingDocker(record, "net1")));
      var context = new DriverContext("docker");
      driver.Initialize(context);

      var result = await driver.CreateAsync(context, new NetworkCreateConfig
      {
        Name = "net",
        IpRange = "172.20.10.0/24"
      }, TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      Assert.Contains("--ip-range\n172.20.10.0/24", await File.ReadAllTextAsync(record, TestContext.Current.CancellationToken));
    }
  }
}
