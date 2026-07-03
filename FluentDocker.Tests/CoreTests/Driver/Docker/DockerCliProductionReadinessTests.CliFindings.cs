using System;
using System.Globalization;
using System.IO;
using System.Threading;
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
    public async Task CopyAndImport_PositionalPaths_PreserveBackslashSpacesAndQuotes()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var record = Path.Combine(TestOutputDirectory(), $"args-{Guid.NewGuid():N}.txt");
      var docker = CreateRecordingDocker(record, "ok");
      var container = new DockerCliContainerDriver(new FakeResolver(docker));
      var image = new DockerCliImageDriver(new FakeResolver(docker));
      container.Initialize(new DriverContext("docker"));
      image.Initialize(new DriverContext("docker"));

      var path = "C:\\Temp\\with space\\quoted\"name\\";

      Assert.True((await container.CopyToAsync(new DriverContext("docker"), "abc123", path, "/app/data", TestContext.Current.CancellationToken)).Success);
      Assert.Equal(["cp", path, "abc123:/app/data"], await ReadArgsAsync(record));

      Assert.True((await container.CopyFromAsync(new DriverContext("docker"), "abc123", "/app/data", path, TestContext.Current.CancellationToken)).Success);
      Assert.Equal(["cp", "abc123:/app/data", path], await ReadArgsAsync(record));

      Assert.True((await container.ExportAsync(new DriverContext("docker"), "abc123", path, TestContext.Current.CancellationToken)).Success);
      Assert.Equal(["export", "-o", path, "abc123"], await ReadArgsAsync(record));

      Assert.True((await image.ImportAsync(new DriverContext("docker"), path, message: "msg with \"quote\"", cancellationToken: TestContext.Current.CancellationToken)).Success);
      Assert.Equal(["import", "-m", "msg with \"quote\"", path], await ReadArgsAsync(record));
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
    public async Task UnboundedCommand_OutputBeyondCap_KeepsTailMarkerAndExitCode()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell semantics; not applicable on Windows");

      var driver = new ShellDriver();
      driver.Initialize(new DriverContext("docker"));

      var result = await driver.RunUnbounded(
          "-c \"yes head | head -c 5000000; printf TAIL; exit 7\"",
          TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(7, result.ExitCode);
      Assert.Contains("[FluentDocker: output truncated", result.Output);
      Assert.EndsWith("TAIL", result.Output);
      Assert.True(result.Output.Length < 512 * 1024);
    }

    [Fact]
    public async Task PerCallContext_ReachesContainerComposeAndNetworkDrivers()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var record = Path.Combine(TestOutputDirectory(), $"args-{Guid.NewGuid():N}.txt");
      var docker = CreateRecordingDocker(record, "{}");
      var context = new DriverContext("docker") { Host = "tcp://per-call:2375" };

      var container = new DockerCliContainerDriver(new FakeResolver(docker));
      container.Initialize(new DriverContext("docker") { Host = "tcp://component:2375" });
      Assert.True((await container.StartAsync(context, "abc123", TestContext.Current.CancellationToken)).Success);
      Assert.Contains("tcp://per-call:2375", await File.ReadAllTextAsync(record, TestContext.Current.CancellationToken));

      var compose = new DockerCliComposeDriver(new FakeResolver(docker));
      compose.Initialize(new DriverContext("docker") { Host = "tcp://component:2375" });
      Assert.True((await compose.ListAsync(context, new ComposeListConfig(), TestContext.Current.CancellationToken)).Success);
      Assert.Contains("tcp://per-call:2375", await File.ReadAllTextAsync(record, TestContext.Current.CancellationToken));

      var network = new DockerCliNetworkDriver(new FakeResolver(docker));
      network.Initialize(new DriverContext("docker") { Host = "tcp://component:2375" });
      Assert.True((await network.ListAsync(context, cancellationToken: TestContext.Current.CancellationToken)).Success);
      Assert.Contains("tcp://per-call:2375", await File.ReadAllTextAsync(record, TestContext.Current.CancellationToken));
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

    [Fact]
    public async Task StartAsync_PreservesTimeoutAndClassifiesDaemonConnectionErrors()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var timeoutDriver = new DockerCliContainerDriver(new FakeResolver(CreateFakeDocker("""
#!/bin/sh
sleep 30
""")));
      timeoutDriver.Initialize(new DriverContext("docker") { RequestTimeout = TimeSpan.FromMilliseconds(200) });

      var timeout = await timeoutDriver.StartAsync(new DriverContext("docker"), "abc123", CancellationToken.None);
      Assert.False(timeout.Success);
      Assert.Equal(ErrorCodes.General.Timeout, timeout.ErrorCode);

      var daemonDriver = new DockerCliContainerDriver(new FakeResolver(CreateFakeDocker("""
#!/bin/sh
echo 'Cannot connect to the Docker daemon at unix:///var/run/docker.sock' 1>&2
exit 1
""")));
      daemonDriver.Initialize(new DriverContext("docker"));

      var daemon = await daemonDriver.StartAsync(new DriverContext("docker"), "abc123", TestContext.Current.CancellationToken);
      Assert.False(daemon.Success);
      Assert.Equal(ErrorCodes.Api.ConnectionFailed, daemon.ErrorCode);

      var missingDriver = new DockerCliContainerDriver(new FakeResolver(CreateFakeDocker("""
#!/bin/sh
echo 'Error response from daemon: No such container: abc123' 1>&2
exit 1
""")));
      missingDriver.Initialize(new DriverContext("docker"));

      var missing = await missingDriver.StartAsync(new DriverContext("docker"), "abc123", TestContext.Current.CancellationToken);
      Assert.False(missing.Success);
      Assert.Equal(ErrorCodes.Container.StartFailed, missing.ErrorCode);
    }

    [Fact]
    public async Task ExecAsync_ClassifiesDaemonConnectionError()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var driver = new DockerCliContainerDriver(new FakeResolver(CreateFakeDocker("""
#!/bin/sh
echo 'Cannot connect to the Docker daemon at unix:///var/run/docker.sock' 1>&2
exit 1
""")));
      driver.Initialize(new DriverContext("docker"));

      var result = await driver.ExecAsync(
          new DriverContext("docker"),
          "abc123",
          new ExecConfig { Command = ["true"] },
          TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Api.ConnectionFailed, result.ErrorCode);
    }

    [Fact]
    public async Task CreateAndComposeLongOperations_DoNotUseBufferedTimeout()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var docker = CreateFakeDocker("""
#!/bin/sh
sleep 1
case "$*" in
  *"compose up"*) echo up; exit 0 ;;
  *"compose run"*) echo run; exit 0 ;;
  *"compose exec"*) echo exec; exit 0 ;;
  create*) echo abc123; exit 0 ;;
esac
exit 2
""");
      var context = new DriverContext("docker") { RequestTimeout = TimeSpan.FromMilliseconds(100) };

      var container = new DockerCliContainerDriver(new FakeResolver(docker));
      container.Initialize(context);
      Assert.True((await container.CreateAsync(context, new ContainerCreateConfig { Image = "alpine" }, TestContext.Current.CancellationToken)).Success);

      var compose = new DockerCliComposeDriver(new FakeResolver(docker));
      compose.Initialize(context);
      Assert.True((await compose.UpAsync(context, new ComposeUpConfig(), TestContext.Current.CancellationToken)).Success);
      Assert.True((await compose.RunAsync(context, new ComposeRunConfig { Service = "web" }, TestContext.Current.CancellationToken)).Success);
      Assert.True((await compose.ExecuteAsync(context, new ComposeExecConfig { Service = "web", Command = ["true"] }, TestContext.Current.CancellationToken)).Success);
    }

    [Fact]
    public async Task FiltersAndHealthDurations_AreSingleArguments()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var record = Path.Combine(TestOutputDirectory(), $"args-{Guid.NewGuid():N}.txt");
      var driver = new DockerCliContainerDriver(new FakeResolver(CreateRecordingDocker(record, "{}")));
      driver.Initialize(new DriverContext("docker"));

      var list = await driver.ListAsync(new DriverContext("docker"), new ContainerListFilter
      {
        Name = "name with \"quote\""
      }, TestContext.Current.CancellationToken);
      Assert.True(list.Success, list.Error);
      Assert.Equal(["ps", "--format", "{{json .}}", "--filter", "name=name with \"quote\""], await ReadArgsAsync(record));

      var create = await driver.CreateAsync(new DriverContext("docker"), new ContainerCreateConfig
      {
        Image = "alpine",
        HealthCheck = new HealthCheckConfig { Interval = "1s --privileged" }
      }, TestContext.Current.CancellationToken);
      Assert.True(create.Success, create.Error);
      Assert.Contains("--health-interval\n1s --privileged", await File.ReadAllTextAsync(record, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WaitAsync_InvalidExitCode_Fails()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var driver = new DockerCliContainerDriver(new FakeResolver(CreateFakeDocker("""
#!/bin/sh
echo not-an-int
exit 0
""")));
      driver.Initialize(new DriverContext("docker"));

      var result = await driver.WaitAsync(new DriverContext("docker"), "abc123", TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Container.WaitFailed, result.ErrorCode);
    }

    [Fact]
    public async Task ImageList_ParsesCliDateUsingInvariantCulture()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var originalCulture = CultureInfo.CurrentCulture;
      try
      {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
        var driver = new DockerCliImageDriver(new FakeResolver(CreateFakeDocker("""
#!/bin/sh
printf '%s\n' '{"ID":"sha256:abc","Repository":"repo","Tag":"latest","Size":"1B","CreatedAt":"12/31/2024 1:02:03 PM"}'
""")));
        driver.Initialize(new DriverContext("docker"));

        var result = await driver.ListAsync(new DriverContext("docker"), cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        Assert.Equal(2024, Assert.Single(result.Data).Created.Year);
      }
      finally
      {
        CultureInfo.CurrentCulture = originalCulture;
      }
    }

    [Fact]
    public async Task StatsAsync_InvalidJson_FailsInsteadOfReturningZeroStats()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var driver = new DockerCliContainerDriver(new FakeResolver(CreateFakeDocker("""
#!/bin/sh
echo not-json
exit 0
""")));
      driver.Initialize(new DriverContext("docker"));

      var result = await driver.StatsAsync(new DriverContext("docker"), "abc123", TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Container.StatsFailed, result.ErrorCode);
    }

    [Fact]
    public void BuildGlobalArgs_CertificatePathWithoutHost_StillEmitsTlsFlags()
    {
      var result = DockerCliDriverBase.BuildGlobalArgs(new DriverContext("docker")
      {
        CertificatePath = "/certs",
        VerifyTls = true
      });

      Assert.DoesNotContain("-H", result);
      Assert.Contains("--tlsverify", result);
      Assert.Contains("ca.pem", result);
    }

    [Fact]
    public async Task RunAsync_ForegroundOutputIncludesStderr()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var driver = new DockerCliContainerDriver(new FakeResolver(CreateFakeDocker("""
#!/bin/sh
echo abc123 > "$3"
echo stdout
echo stderr 1>&2
exit 0
""")));
      driver.Initialize(new DriverContext("docker"));

      var result = await driver.RunAsync(new DriverContext("docker"), new ContainerCreateConfig
      {
        Image = "alpine",
        Detach = false
      }, TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      Assert.Contains("stdout", result.Data.Output);
      Assert.Contains("stderr", result.Data.Output);
    }

    [Fact]
    public async Task LeadingDashPositionals_FailBeforeDockerParsesThemAsFlags()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var driver = new DockerCliContainerDriver(new FakeResolver(CreateFakeDocker("""
#!/bin/sh
echo should-not-run
exit 0
""")));
      driver.Initialize(new DriverContext("docker"));

      var result = await driver.CreateAsync(new DriverContext("docker"), new ContainerCreateConfig
      {
        Image = "-alpine"
      }, TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.General.InvalidArgument, result.ErrorCode);

      var copy = await driver.CopyToAsync(
          new DriverContext("docker"),
          "-bad",
          "/host",
          "/container",
          TestContext.Current.CancellationToken);

      Assert.False(copy.Success);
      Assert.Equal(ErrorCodes.General.InvalidArgument, copy.ErrorCode);
    }

  }
}
