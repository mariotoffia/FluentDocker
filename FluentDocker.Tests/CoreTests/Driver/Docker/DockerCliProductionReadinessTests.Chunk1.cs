using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Cli.Components;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Docker
{
  public sealed partial class DockerCliProductionReadinessTests
  {
    [Fact]
    public async Task ContainerInspect_UsesContainerScopedInspect()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var record = Path.Combine(TestOutputDirectory(), $"inspect-args-{Guid.NewGuid():N}.txt");
      var driver = new DockerCliContainerDriver(new FakeResolver(CreateRecordingDocker(record, "[{}]")));
      driver.Initialize(new DriverContext("docker"));

      var result = await driver.InspectAsync(new DriverContext("docker"), "redis", TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      Assert.Equal(["container", "inspect", "redis"], await ReadArgsAsync(record));
    }

    [Fact]
    public async Task ServiceTasks_AppliesDesiredStateFilter()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var record = Path.Combine(TestOutputDirectory(), $"service-ps-args-{Guid.NewGuid():N}.txt");
      var driver = new DockerCliServiceDriver(new FakeResolver(CreateRecordingDocker(record, "")));
      driver.Initialize(new DriverContext("docker"));

      var result = await driver.GetTasksAsync(
          new DriverContext("docker"),
          "web",
          new ServiceTaskFilter { DesiredState = "running" },
          TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      Assert.Equal(
          ["service", "ps", "--filter", "desired-state=running", "--format", "{{json .}}", "web"],
          await ReadArgsAsync(record));
    }

    [Fact]
    public async Task ServiceTasks_QuietProjectsToIdOnly()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var driver = new DockerCliServiceDriver(new FakeResolver(CreateFakeDocker("""
#!/bin/sh
printf '%s\n' '{"ID":"t1","Name":"web.1"}' '{"ID":"t2","Name":"web.2"}'
exit 0
""")));
      driver.Initialize(new DriverContext("docker"));

      var result = await driver.GetTasksAsync(
          new DriverContext("docker"),
          "web",
          new ServiceTaskFilter { Quiet = true },
          TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      Assert.Collection(
          result.Data,
          t => { Assert.Equal("t1", t.Id); Assert.Null(t.Name); },
          t => { Assert.Equal("t2", t.Id); Assert.Null(t.Name); });
    }

    [Fact]
    public async Task SwitchDaemonAsync_UsesBufferedTimeoutForDefaultToken()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var driver = new DockerCliSystemDriver(new FakeResolver(CreateFakeDocker("""
#!/bin/sh
sleep 30
""")));
      driver.Initialize(new DriverContext("docker"));

      var result = await driver.SwitchDaemonAsync(
          new DriverContext("docker") { RequestTimeout = TimeSpan.FromMilliseconds(200) },
          CancellationToken.None);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.General.Timeout, result.ErrorCode);
    }

    [Fact]
    public async Task StartAsync_ClassifiesDocker29DaemonConnectionError()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var driver = new DockerCliContainerDriver(new FakeResolver(CreateFakeDocker("""
#!/bin/sh
echo 'failed to connect to the docker API at unix:///Users/user/.docker/run/docker.sock' 1>&2
exit 1
""")));
      driver.Initialize(new DriverContext("docker"));

      var result = await driver.StartAsync(new DriverContext("docker"), "abc123", TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Api.ConnectionFailed, result.ErrorCode);
    }

    [Fact]
    public async Task DetachedRun_Cancellation_RemovesCidFileContainer()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var record = Path.Combine(TestOutputDirectory(), $"run-cancel-{Guid.NewGuid():N}.txt");
      var driver = new DockerCliContainerDriver(new FakeResolver(CreateFakeDocker($$"""
#!/bin/sh
printf '%s\n' "$*" >> '{{record}}'
if [ "$1" = "run" ]; then
  while [ "$1" != "" ]; do
    if [ "$1" = "--cidfile" ]; then
      shift
      printf 'detached-123\n' > "$1"
  printf 'cid-written\n' >> '{{record}}'
  break
    fi
    shift
  done
  sleep 30
fi
exit 0
""")));
      driver.Initialize(new DriverContext("docker"));

      using var cts = new CancellationTokenSource();
      var run = driver.RunAsync(
          new DriverContext("docker"),
          new ContainerCreateConfig { Image = "alpine", Detach = true },
          cts.Token);
      for (var i = 0; i < 500 && (!File.Exists(record) || !File.ReadAllText(record).Contains("cid-written", StringComparison.Ordinal)); i++)
        await Task.Delay(20, TestContext.Current.CancellationToken);
      Assert.True(File.Exists(record), "fake docker did not start");
      cts.Cancel();

      await Assert.ThrowsAsync<OperationCanceledException>(() => run);

      var commands = await File.ReadAllTextAsync(record, TestContext.Current.CancellationToken);
      Assert.Contains("run -d --cidfile", commands, StringComparison.Ordinal);
      Assert.Contains("rm -f detached-123", commands, StringComparison.Ordinal);
    }

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
