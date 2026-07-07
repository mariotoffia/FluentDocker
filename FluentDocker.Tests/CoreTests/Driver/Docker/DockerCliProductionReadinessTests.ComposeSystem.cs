using System;
using System.Diagnostics;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Cli.Components;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Models;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Docker
{
  public sealed partial class DockerCliProductionReadinessTests
  {
    [Fact]
    public async Task ComposeConfig_PropagatesComposeEnvironment()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var driver = new DockerCliComposeDriver(new FakeResolver(CreateFakeDocker("""
#!/bin/sh
printf '%s' "$FD_COMPOSE_ENV"
exit 0
""")));
      driver.Initialize(new DriverContext("docker"));

      var result = await driver.ConfigAsync(new DriverContext("docker"), new ComposeConfigConfig
      {
        Environment = new System.Collections.Generic.Dictionary<string, string> { { "FD_COMPOSE_ENV", "from-config" } }
      }, TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      Assert.Equal("from-config", result.Data);
    }

    [Fact]
    public async Task ComposeDown_DoesNotUseBufferedTimeout()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var driver = new DockerCliComposeDriver(new FakeResolver(CreateFakeDocker("""
#!/bin/sh
sleep 1
exit 0
""")));
      var context = new DriverContext("docker") { RequestTimeout = TimeSpan.FromMilliseconds(100) };
      driver.Initialize(context);

      var result = await driver.DownAsync(context, new ComposeDownConfig(), TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
    }

    [Fact]
    public async Task ComposeGetLogsAsync_ChattyOutput_ReturnsMarkedTailInsteadOfFailing()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var driver = new DockerCliComposeDriver(new FakeResolver(CreateFakeDocker("""
#!/bin/sh
head -c 5242880 /dev/zero | tr '\0' x
echo
echo TAIL
exit 0
""")));
      driver.Initialize(new DriverContext("docker"));

      var result = await driver.GetLogsAsync(new DriverContext("docker"), new ComposeLogsConfig(), TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      Assert.Contains("[FluentDocker: output truncated", result.Data);
      Assert.EndsWith("TAIL\n", result.Data);
    }

    [Fact]
    public async Task ComposeGetLogsAsync_Follow_ReturnsExplicitUnsupportedFailure()
    {
      var driver = new DockerCliComposeDriver(new FakeResolver("docker"));

      var result = await driver.GetLogsAsync(new DriverContext("docker"), new ComposeLogsConfig
      {
        Follow = true
      }, TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(
          "Compose GetLogsAsync follow=true is not supported by this buffered method; use a streaming logs API instead.",
          result.Error);
    }

    [Fact]
    public async Task ModelPullAsync_StreamedCommandFailure_ReturnsPullFailed()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var driver = new DockerCliModelManagementDriver(new FakeResolver(CreateFakeDocker("""
#!/bin/sh
if [ "$1" = "model" ] && [ "$2" = "pull" ]; then
  echo "registry unavailable" >&2
  exit 1
fi
exit 0
""")));
      driver.Initialize(new DriverContext("docker"));

      var result = await driver.PullAsync(new DriverContext("docker"),
          ModelReference.Parse("ai/missing"), cancellationToken: TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Model.PullFailed, result.ErrorCode);
    }

    [Fact]
    public async Task ModelPullAsync_PluginMissingStreamedFailure_ReturnsPluginMissing()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var driver = new DockerCliModelManagementDriver(new FakeResolver(CreateFakeDocker("""
#!/bin/sh
if [ "$1" = "model" ] && [ "$2" = "pull" ]; then
  echo "docker: 'model' is not a docker command" >&2
  exit 1
fi
exit 0
""")));
      driver.Initialize(new DriverContext("docker"));

      var result = await driver.PullAsync(new DriverContext("docker"),
          ModelReference.Parse("ai/missing"), cancellationToken: TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Model.PluginMissing, result.ErrorCode);
    }

    [Fact]
    public async Task ModelLoadAsync_DoesNotUseBufferedTimeout()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var driver = new DockerCliModelRuntimeDriver(new FakeResolver(CreateFakeDocker("""
#!/bin/sh
if [ "$1" = "model" ] && [ "$2" = "run" ]; then
  sleep 1
  exit 0
fi
exit 0
""")));
      var context = new DriverContext("docker") { RequestTimeout = TimeSpan.FromMilliseconds(100) };
      driver.Initialize(context);

      var result = await driver.LoadAsync(context,
          ModelReference.Parse("ai/smollm2"), cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
    }

    [Fact]
    public async Task InspectDrivers_InvalidJson_ReturnParseFailures()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var docker = CreateFakeDocker("""
#!/bin/sh
echo not-json
exit 0
""");
      var context = new DriverContext("docker");
      var container = new DockerCliContainerDriver(new FakeResolver(docker));
      var image = new DockerCliImageDriver(new FakeResolver(docker));
      var network = new DockerCliNetworkDriver(new FakeResolver(docker));
      var volume = new DockerCliVolumeDriver(new FakeResolver(docker));
      container.Initialize(context);
      image.Initialize(context);
      network.Initialize(context);
      volume.Initialize(context);

      var containerResult = await container.InspectAsync(context, "ctr", TestContext.Current.CancellationToken);
      var imageResult = await image.InspectAsync(context, "img", TestContext.Current.CancellationToken);
      var networkResult = await network.InspectAsync(context, "net", TestContext.Current.CancellationToken);
      var volumeResult = await volume.InspectAsync(context, "vol", TestContext.Current.CancellationToken);

      Assert.False(containerResult.Success);
      Assert.False(imageResult.Success);
      Assert.False(networkResult.Success);
      Assert.False(volumeResult.Success);
      Assert.Contains("Container inspect JSON parsing failed", containerResult.Error);
      Assert.Contains("Image inspect JSON parsing failed", imageResult.Error);
      Assert.Contains("Network inspect JSON parsing failed", networkResult.Error);
      Assert.Contains("Volume inspect JSON parsing failed", volumeResult.Error);
    }

    [Fact]
    public async Task SwitchDaemonAsync_CancellationDoesNotWaitForPollingInterval()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var driver = new DockerCliSystemDriver(new FakeResolver(CreateFakeDocker("""
#!/bin/sh
sleep 30
exit 0
""")));
      using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromMilliseconds(50));
      var sw = Stopwatch.StartNew();

      await Assert.ThrowsAnyAsync<OperationCanceledException>(() => driver.SwitchDaemonAsync(new DriverContext("docker"), cts.Token));

      Assert.True(sw.Elapsed < TimeSpan.FromMilliseconds(500), $"Cancellation took {sw.Elapsed}.");
    }
  }
}
