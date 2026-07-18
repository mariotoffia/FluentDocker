using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Cli.Components;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Docker
{
  /// <summary>
  /// Production-readiness coverage for <see cref="DockerCliServiceDriver"/>: task/service filters,
  /// quiet id-only projection, buffered-timeout exclusion for non-detached create and merged
  /// stdout/stderr log capture.
  /// </summary>
  [Trait("Category", "Unit")]
  [Trait("Requires", "PosixShell")]
  public sealed class DockerCliServiceDriverProductionReadinessTests : DockerCliFakeDockerTestBase
  {
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
    public async Task ServiceCreate_NonDetached_IgnoresBufferedRequestTimeout()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var driver = new DockerCliServiceDriver(new FakeResolver(CreateFakeDocker("""
#!/bin/sh
sleep 0.3
echo service-id
exit 0
""")));
      var context = new DriverContext("docker") { RequestTimeout = TimeSpan.FromMilliseconds(50) };
      driver.Initialize(context);

      var result = await driver.CreateAsync(
          context,
          new ServiceCreateConfig { Image = "alpine", Detach = false },
          TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      Assert.Equal("service-id", result.Data.Id);
    }

    [Fact]
    public async Task ServiceList_QuietStillParsesJsonAndAppliesFilters()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var record = Path.Combine(TestOutputDirectory(), $"service-list-{Guid.NewGuid():N}.txt");
      var driver = new DockerCliServiceDriver(new FakeResolver(CreateFakeDocker($$"""
#!/bin/sh
printf '%s\n' "$@" > "{{record}}"
printf '%s\n' '{"ID":"svc1","Name":"web","Mode":"replicated","Replicas":"1/1","Image":"nginx"}'
printf '%s\n' '{"ID":"svc2","Name":"api","Mode":"global","Replicas":"2/2","Image":"redis"}'
""")));
      driver.Initialize(new DriverContext("docker"));

      var quiet = await driver.ListAsync(new DriverContext("docker"), new ServiceListFilter
      {
        Quiet = true,
        Name = "web",
        Id = "svc",
        Mode = "replicated",
        Labels = { { "tier", "frontend" } }
      }, TestContext.Current.CancellationToken);
      var quietArgs = await ReadArgsAsync(record);

      var full = await driver.ListAsync(new DriverContext("docker"), null, TestContext.Current.CancellationToken);

      Assert.True(quiet.Success, quiet.Error);
      Assert.Equal(["svc1", "svc2"], quiet.Data.Select(s => s.Id));
      Assert.All(quiet.Data, s => Assert.Null(s.Name));
      Assert.Contains("--format", quietArgs);
      Assert.Contains("{{json .}}", quietArgs);
      Assert.Contains("name=web", quietArgs);
      Assert.Contains("id=svc", quietArgs);
      Assert.Contains("label=tier=frontend", quietArgs);
      Assert.Contains("mode=replicated", quietArgs);
      Assert.True(full.Success, full.Error);
      Assert.Equal("web", full.Data[0].Name);
    }

    [Fact]
    public async Task ServiceGetLogsAsync_MergesStdoutAndStderr()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var driver = new DockerCliServiceDriver(new FakeResolver(CreateFakeDocker("""
#!/bin/sh
if [ "$1" = "service" ] && [ "$2" = "logs" ]; then
  printf 'stdout-line\n'
  printf 'stderr-line\n' 1>&2
  exit 0
fi
exit 2
""")));
      driver.Initialize(new DriverContext("docker"));

      var result = await driver.GetLogsAsync(
          new DriverContext("docker"),
          "svc",
          cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      Assert.Contains("stdout-line", result.Data);
      Assert.Contains("stderr-line", result.Data);
    }
  }
}
