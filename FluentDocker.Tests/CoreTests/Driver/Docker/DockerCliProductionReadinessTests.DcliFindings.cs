using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Cli;
using FluentDocker.Drivers.Docker.Cli.Components;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Docker
{
  public sealed partial class DockerCliProductionReadinessTests
  {
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

    [Fact]
    public async Task ComposeListAsync_QuietProjectsJsonRowsToContainerIds()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var driver = new DockerCliComposeDriver(new FakeResolver(CreateFakeDocker("""
#!/bin/sh
if [ "$1" = "compose" ] && [ "$2" = "ps" ]; then
  printf '%s\n' '{"ID":"abc123","Name":"proj-web-1","Service":"web","State":"running"}'
  printf '%s\n' '{"ID":"def456","Name":"proj-db-1","Service":"db","State":"running"}'
  exit 0
fi
exit 2
""")));
      driver.Initialize(new DriverContext("docker"));

      var result = await driver.ListAsync(
          new DriverContext("docker"),
          new ComposeListConfig { Quiet = true },
          TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      Assert.Equal(["abc123", "def456"], result.Data.Select(s => s.ContainerId));
      Assert.All(result.Data, s => Assert.Null(s.Name));
      Assert.All(result.Data, s => Assert.Null(s.ContainerName));
    }

    [Fact]
    public async Task SystemGetInfoAsync_MalformedJsonReturnsParseFailureMessage()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var driver = new DockerCliSystemDriver(new FakeResolver(CreateFakeDocker("""
#!/bin/sh
if [ "$1" = "info" ]; then
  printf '{'
  exit 0
fi
exit 2
""")));
      driver.Initialize(new DriverContext("docker"));

      var result = await driver.GetInfoAsync(new DriverContext("docker"), TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Contains("System info JSON parsing failed", result.Error);
    }

    [Fact]
    public async Task SystemGetVersionAsync_MalformedJsonReturnsParseFailureMessage()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var driver = new DockerCliSystemDriver(new FakeResolver(CreateFakeDocker("""
#!/bin/sh
if [ "$1" = "version" ]; then
  printf '{'
  exit 0
fi
exit 2
""")));
      driver.Initialize(new DriverContext("docker"));

      var result = await driver.GetVersionAsync(new DriverContext("docker"), TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Contains("Docker version JSON parsing failed", result.Error);
    }

    [Fact]
    public async Task DriverPackGetSupportedInterfaces_ReturnsCachedCollectionAfterInitialize()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var dir = CreateNamedFakeDockerDirectory("""
#!/bin/sh
exit 0
""");
      var pack = new DockerCliDriverPack();

      await pack.InitializeAsync(
          new DriverContext("docker") { SearchPaths = [dir] },
          TestContext.Current.CancellationToken);

      Assert.Same(pack.GetSupportedInterfaces(), pack.GetSupportedInterfaces());
    }

    private static string CreateNamedFakeDockerDirectory(string script)
    {
      var directory = Path.Combine(TestOutputDirectory(), $"named-docker-{Guid.NewGuid():N}");
      Directory.CreateDirectory(directory);
      var path = Path.Combine(directory, "docker");
      File.WriteAllText(path, script.Replace("\r\n", "\n"));
      using var chmod = Process.Start(new ProcessStartInfo
      {
        FileName = "chmod",
        UseShellExecute = false,
        CreateNoWindow = true,
        ArgumentList = { "+x", path }
      });
      chmod!.WaitForExit();
      return directory;
    }
  }
}
