using System;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Cli.Components;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Models;
using FluentDocker.Model.Models.Options;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Docker
{
  /// <summary>
  /// Production-readiness coverage for the Docker Model Runner drivers
  /// (<see cref="DockerCliModelManagementDriver"/> and <see cref="DockerCliModelRuntimeDriver"/>):
  /// buffered-timeout exclusion for long-running push/package/install/load, and streamed pull
  /// failure classification (registry failure vs missing model plugin).
  /// </summary>
  [Trait("Category", "Unit")]
  [Trait("Requires", "PosixShell")]
  public sealed class DockerCliModelDriverProductionReadinessTests : DockerCliFakeDockerTestBase
  {
    [Fact]
    public async Task ModelLargeOperations_DoNotUseBufferedTimeout()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var docker = CreateFakeDocker("""
#!/bin/sh
sleep 1
exit 0
""");
      var context = new DriverContext("docker") { RequestTimeout = TimeSpan.FromMilliseconds(100) };
      var management = new DockerCliModelManagementDriver(new FakeResolver(docker));
      var runtime = new DockerCliModelRuntimeDriver(new FakeResolver(docker));
      management.Initialize(context);
      runtime.Initialize(context);

      var push = await management.PushAsync(context, ModelReference.Parse("ai/smollm2"), TestContext.Current.CancellationToken);
      var package = await management.PackageAsync(context, new ModelPackageRequest
      {
        Target = ModelReference.Parse("ai/smollm2")
      }, TestContext.Current.CancellationToken);
      var install = await runtime.InstallRunnerAsync(context, cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(push.Success, push.Error);
      Assert.True(package.Success, package.Error);
      Assert.True(install.Success, install.Error);
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
  }
}
