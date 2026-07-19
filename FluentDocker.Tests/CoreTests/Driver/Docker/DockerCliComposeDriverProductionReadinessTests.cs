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
  /// Production-readiness coverage for <see cref="DockerCliComposeDriver"/>: argument quoting,
  /// leading-dash guards, buffered-timeout exclusions for long-running operations, quiet/JSON
  /// projection, port/protocol emission, chatty-log truncation and top-output parsing.
  /// </summary>
  [Trait("Category", "Unit")]
  [Trait("Requires", "PosixShell")]
  public sealed class DockerCliComposeDriverProductionReadinessTests : DockerCliFakeDockerTestBase
  {
    [Fact]
    public async Task ComposeExec_InContainerNonZeroExit_IsReturnedAsCommandResult()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var driver = new DockerCliComposeDriver(new FakeResolver(CreateFakeDocker("""
#!/bin/sh
echo stdout
echo stderr >&2
exit 5
""")));
      driver.Initialize(new DriverContext("docker"));

      var result = await driver.ExecuteAsync(
          new DriverContext("docker"),
          new ComposeExecConfig { Service = "web", Tty = false, Command = ["false"] },
          TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      Assert.Equal(5, result.ExitCode);
      Assert.Contains("stdout", result.Data);
    }

    [Fact]
    public async Task ComposeCopy_RejectsLeadingDashSourceAndDestination()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var driver = new DockerCliComposeDriver(new FakeResolver(CreateRecordingDocker(
          Path.Combine(TestOutputDirectory(), $"compose-cp-{Guid.NewGuid():N}.txt"),
          "ok")));
      driver.Initialize(new DriverContext("docker"));

      var source = await driver.CopyAsync(new DriverContext("docker"), new ComposeCopyConfig
      {
        Source = "--help",
        Destination = "svc:/data"
      }, TestContext.Current.CancellationToken);
      var destination = await driver.CopyAsync(new DriverContext("docker"), new ComposeCopyConfig
      {
        Source = "svc:/data",
        Destination = "--help"
      }, TestContext.Current.CancellationToken);

      Assert.False(source.Success);
      Assert.Equal(ErrorCodes.General.InvalidArgument, source.ErrorCode);
      Assert.False(destination.Success);
      Assert.Equal(ErrorCodes.General.InvalidArgument, destination.ErrorCode);
    }

    [Fact]
    public async Task ComposeCopy_DoesNotUseBufferedTimeout()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var driver = new DockerCliComposeDriver(new FakeResolver(CreateFakeDocker("""
#!/bin/sh
sleep 1
echo copied
""")));
      var context = new DriverContext("docker") { RequestTimeout = TimeSpan.FromMilliseconds(100) };
      driver.Initialize(context);

      var result = await driver.CopyAsync(context, new ComposeCopyConfig
      {
        Source = "svc:/data",
        Destination = ".out/copied"
      }, TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
    }

    [Fact]
    public async Task ComposeExec_QuotesServiceAndCommandElements()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var record = Path.Combine(TestOutputDirectory(), $"args-{Guid.NewGuid():N}.txt");
      var driver = new DockerCliComposeDriver(new FakeResolver(CreateRecordingDocker(record, "done")));
      driver.Initialize(new DriverContext("docker"));

      var result = await driver.ExecuteAsync(new DriverContext("docker"), new ComposeExecConfig
      {
        Service = "web api",
        Command = ["sh", "-c", "echo a b"]
      }, TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      Assert.Equal(["compose", "exec", "web api", "sh", "-c", "echo a b"], await ReadArgsAsync(record));
    }

    [Fact]
    public async Task ComposeRun_InContainerNonZeroExit_IsReturnedAsCommandResult()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var driver = new DockerCliComposeDriver(new FakeResolver(CreateFakeDocker("""
#!/bin/sh
echo stdout
echo stderr >&2
exit 5
""")));
      driver.Initialize(new DriverContext("docker"));

      var result = await driver.RunAsync(
          new DriverContext("docker"),
          new ComposeRunConfig { Service = "web", Tty = false, Command = ["false"] },
          TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      Assert.Equal(5, result.ExitCode);
      Assert.Equal("stdout\n", result.Data);
      Assert.Equal("stdout\nstderr\n", result.Output);
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
    public async Task ComposeTopAsync_JoinsPsJsonToServiceAndContainerId()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var driver = new DockerCliComposeDriver(new FakeResolver(CreateFakeDocker("""
#!/bin/sh
if [ "$1" = "compose" ] && [ "$2" = "top" ]; then
  printf '%s\n' 'proj-web-1' 'UID    PID     CMD' 'root   1       nginx'
  exit 0
fi
if [ "$1" = "compose" ] && [ "$2" = "ps" ]; then
  printf '%s\n' '{"ID":"abc123def","Name":"proj-web-1","Service":"web","State":"running"}'
  exit 0
fi
exit 2
""")));
      driver.Initialize(new DriverContext("docker"));

      var result = await driver.TopAsync(
          new DriverContext("docker"),
          new ComposeFileConfig(),
          TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      var processes = Assert.Single(result.Data);
      Assert.Equal("web", processes.Service);
      Assert.Equal("abc123def", processes.ContainerId);
      Assert.Equal("proj-web-1", processes.ContainerName);
    }

    [Fact]
    public async Task ComposePort_NullProtocol_OmitsProtocolFlag()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var record = Path.Combine(TestOutputDirectory(), $"compose-port-{Guid.NewGuid():N}.txt");
      var driver = new DockerCliComposeDriver(new FakeResolver(CreateRecordingDocker(record, "0.0.0.0:8080")));
      driver.Initialize(new DriverContext("docker"));

      var result = await driver.PortAsync(new DriverContext("docker"), new ComposePortConfig
      {
        ComposeFiles = ["compose.yml"],
        Service = "web",
        PrivatePort = 80,
        Protocol = null
      }, TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      Assert.Equal("0.0.0.0:8080", result.Data);
      var args = await ReadArgsAsync(record);
      Assert.DoesNotContain("--protocol", args);
      Assert.Equal(["compose", "-f", "compose.yml", "port", "web", "80"], args);
    }

    [Fact]
    public async Task ComposePort_ExplicitProtocol_EmitsProtocolFlag()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var record = Path.Combine(TestOutputDirectory(), $"compose-port-{Guid.NewGuid():N}.txt");
      var driver = new DockerCliComposeDriver(new FakeResolver(CreateRecordingDocker(record, "0.0.0.0:5353")));
      driver.Initialize(new DriverContext("docker"));

      var result = await driver.PortAsync(new DriverContext("docker"), new ComposePortConfig
      {
        ComposeFiles = ["compose.yml"],
        Service = "dns",
        PrivatePort = 53,
        Protocol = "udp"
      }, TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      var args = await ReadArgsAsync(record);
      Assert.Equal(["compose", "-f", "compose.yml", "port", "--protocol", "udp", "dns", "53"], args);
    }

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
    public async Task ComposeExec_DataIsStdoutOnlyAndOutputIsMerged()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var driver = new DockerCliComposeDriver(new FakeResolver(CreateFakeDocker("""
#!/bin/sh
echo stdout
echo stderr 1>&2
exit 0
""")));
      driver.Initialize(new DriverContext("docker"));

      var result = await driver.ExecuteAsync(new DriverContext("docker"), new ComposeExecConfig
      {
        Service = "db",
        Tty = false,
        Command = ["pg_dump"]
      }, TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      Assert.Equal("stdout\n", result.Data);
      Assert.Equal("stdout\nstderr\n", result.Output);
    }

    [Fact]
    public void ParseTopOutput_UsesHeaderPositionsWhenColumnsAreReordered()
    {
      var output = string.Join("\n", new[]
      {
        "container-a",
        "PID     UID    CMD",
        "1234    root   nginx -g daemon off;"
      });

      var result = DockerCliComposeDriver.ParseTopOutput(output);

      var process = Assert.Single(Assert.Single(result).Processes);
      Assert.Equal("1234", process["PID"]);
      Assert.Equal("root", process["UID"]);
      Assert.Equal("nginx -g daemon off;", process["CMD"]);
    }
  }
}
