using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Cli.Binary;
using FluentDocker.Drivers.Docker.Cli.Components;
using FluentDocker.Model.Common;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Docker
{
  [Trait("Category", "Unit")]
  [Trait("Requires", "PosixShell")]
  public sealed class DockerCliContainerDriverProductionReadinessTests : DockerCliFakeDockerTestBase
  {
    [Fact]
    public async Task CreateAsync_RendersAllBuilderContainerOptions()
    {
      if (OperatingSystem.IsWindows())
        return;

      var directory = Path.Combine(".out", "docker-create-args", Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(directory);
      var dockerPath = Path.Combine(directory, "docker");
      var argsPath = Path.Combine(directory, "args.txt");
      await File.WriteAllTextAsync(dockerPath,
          "#!/bin/sh\n" +
          $"printf '%s\\n' \"$@\" > '{argsPath}'\n" +
          "printf 'container-123\\n'\n",
          TestContext.Current.CancellationToken);
      if (!OperatingSystem.IsWindows())
      {
        File.SetUnixFileMode(dockerPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
      }
      var driver = new DockerCliContainerDriver(new TestBinaryResolver(directory));
      driver.Initialize(new DriverContext("docker"));

      var response = await driver.CreateAsync(
          new DriverContext("docker"),
          new ContainerCreateConfig
          {
            Image = "alpine",
            Name = "web",
            ExtraHosts = new Dictionary<string, string> { { "db", "10.0.0.2" } },
            NetworkAliases = new Dictionary<string, List<string>> { { "net", ["api"] } },
            CapAdd = ["NET_ADMIN"],
            CapDrop = ["MKNOD"],
            SecurityOpt = ["seccomp=unconfined"],
            ShmSize = 67108864,
            Tmpfs = new Dictionary<string, string> { { "/run", "rw,noexec" } },
            Devices = new Dictionary<string, string> { { "/dev/fuse", "/dev/fuse" } },
            ReadonlyRootfs = true,
            Platform = "linux/arm64",
            Runtime = "runc",
            Interactive = true,
            Tty = true,
            Entrypoint = ["/bin/sh", "-c"],
            Command = ["echo", "hello world"]
          },
          TestContext.Current.CancellationToken);

      Assert.True(response.Success, response.Error);
      var args = await File.ReadAllLinesAsync(argsPath, TestContext.Current.CancellationToken);
      Assert.Contains("--add-host", args);
      Assert.Contains("db:10.0.0.2", args);
      Assert.Contains("--network-alias", args);
      Assert.Contains("api", args);
      Assert.Contains("--cap-add", args);
      Assert.Contains("NET_ADMIN", args);
      Assert.Contains("--cap-drop", args);
      Assert.Contains("MKNOD", args);
      Assert.Contains("--security-opt", args);
      Assert.Contains("seccomp=unconfined", args);
      Assert.Contains("--shm-size", args);
      Assert.Contains("67108864", args);
      Assert.Contains("--tmpfs", args);
      Assert.Contains("/run:rw,noexec", args);
      Assert.Contains("--device", args);
      Assert.Contains("/dev/fuse", args);
      Assert.Contains("--read-only", args);
      Assert.Contains("--platform", args);
      Assert.Contains("linux/arm64", args);
      Assert.Contains("--runtime", args);
      Assert.Contains("runc", args);
      Assert.Contains("-i", args);
      Assert.Contains("-t", args);
      Assert.Contains("--entrypoint", args);
      Assert.Contains("/bin/sh", args);
      Assert.Contains("-c", args);
      Assert.Contains("hello world", args);
    }

    [Fact]
    public async Task RunAsync_DoesNotRenderZeroResourceLimits()
    {
      if (OperatingSystem.IsWindows())
        return;

      var directory = Path.Combine(".out", "docker-run-args", Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(directory);
      var dockerPath = Path.Combine(directory, "docker");
      var argsPath = Path.Combine(directory, "args.txt");
      await File.WriteAllTextAsync(dockerPath,
          "#!/bin/sh\n" +
          $"printf '%s\\n' \"$@\" > '{argsPath}'\n" +
          "printf 'container-123\\n'\n",
          TestContext.Current.CancellationToken);
      if (!OperatingSystem.IsWindows())
      {
        File.SetUnixFileMode(dockerPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
      }
      var driver = new DockerCliContainerDriver(new TestBinaryResolver(directory));
      driver.Initialize(new DriverContext("docker"));

      var response = await driver.RunAsync(
          new DriverContext("docker"),
          new ContainerCreateConfig
          {
            Image = "alpine",
            Detach = true,
            MemoryLimit = 0,
            CpuShares = 0,
            ShmSize = 0
          },
          TestContext.Current.CancellationToken);

      Assert.True(response.Success, response.Error);
      var args = await File.ReadAllLinesAsync(argsPath, TestContext.Current.CancellationToken);
      Assert.DoesNotContain("--memory", args);
      Assert.DoesNotContain("--cpu-shares", args);
      Assert.DoesNotContain("--shm-size", args);
    }

    [Fact]
    public async Task InspectAsync_DockerNameWithLeadingSlash_TrimsToBareName()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fixture; not applicable on Windows");

      var directory = Path.Combine(".out", "docker-inspect-name", Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(directory);
      var dockerPath = Path.Combine(directory, "docker");
      await File.WriteAllTextAsync(dockerPath,
          "#!/bin/sh\n" +
          "printf '%s\\n' '[{\"Id\":\"abc123\",\"Name\":\"/foo\"}]'\n",
          TestContext.Current.CancellationToken);
      if (!OperatingSystem.IsWindows())
      {
        File.SetUnixFileMode(dockerPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
      }
      var driver = new DockerCliContainerDriver(new TestBinaryResolver(directory));
      driver.Initialize(new DriverContext("docker"));

      var response = await driver.InspectAsync(
          new DriverContext("docker"),
          "foo",
          TestContext.Current.CancellationToken);

      Assert.True(response.Success, response.Error);
      Assert.Equal("foo", response.Data.Name);
    }

    [Fact]
    public async Task ListAsync_WithLimit_EmitsLastFlag()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fixture; not applicable on Windows");

      var directory = Path.Combine(".out", "docker-list-limit", Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(directory);
      var dockerPath = Path.Combine(directory, "docker");
      var argsPath = Path.Combine(directory, "args.txt");
      await File.WriteAllTextAsync(dockerPath,
          "#!/bin/sh\n" +
          $"printf '%s\\n' \"$@\" > '{argsPath}'\n" +
          "printf '%s\\n' '{\"ID\":\"abc123\",\"Image\":\"alpine\",\"Names\":\"foo\",\"State\":\"running\",\"Status\":\"Up\"}'\n",
          TestContext.Current.CancellationToken);
      if (!OperatingSystem.IsWindows())
      {
        File.SetUnixFileMode(dockerPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
      }
      var driver = new DockerCliContainerDriver(new TestBinaryResolver(directory));
      driver.Initialize(new DriverContext("docker"));

      var response = await driver.ListAsync(
          new DriverContext("docker"),
          new ContainerListFilter { Limit = 3 },
          TestContext.Current.CancellationToken);

      Assert.True(response.Success, response.Error);
      var args = await File.ReadAllLinesAsync(argsPath, TestContext.Current.CancellationToken);
      Assert.Equal(["ps", "--format", "{{json .}}", "--last", "3"], args);
    }

    [Fact]
    public async Task ListAsync_WithNonPositiveLimit_OmitsLastFlag()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fixture; not applicable on Windows");

      var directory = Path.Combine(".out", "docker-list-limit-zero", Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(directory);
      var dockerPath = Path.Combine(directory, "docker");
      var argsPath = Path.Combine(directory, "args.txt");
      await File.WriteAllTextAsync(dockerPath,
          "#!/bin/sh\n" +
          $"printf '%s\\n' \"$@\" > '{argsPath}'\n" +
          "printf '%s\\n' '{\"ID\":\"abc123\",\"Image\":\"alpine\",\"Names\":\"foo\",\"State\":\"running\",\"Status\":\"Up\"}'\n",
          TestContext.Current.CancellationToken);
      if (!OperatingSystem.IsWindows())
      {
        File.SetUnixFileMode(dockerPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
      }
      var driver = new DockerCliContainerDriver(new TestBinaryResolver(directory));
      driver.Initialize(new DriverContext("docker"));

      var response = await driver.ListAsync(
          new DriverContext("docker"),
          new ContainerListFilter { Limit = 0 },
          TestContext.Current.CancellationToken);

      Assert.True(response.Success, response.Error);
      var args = await File.ReadAllLinesAsync(argsPath, TestContext.Current.CancellationToken);
      Assert.DoesNotContain("--last", args);
    }

    [Fact]
    public async Task ContainerList_PreservesCreatedAtOffsets()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var driver = new DockerCliContainerDriver(new FakeResolver(CreateFakeDocker("""
#!/bin/sh
if [ "$1" = "ps" ]; then
  printf '%s\n' '{"Command":"sleep","CreatedAt":"2024-01-02T03:04:05+02:00","ID":"offset","Image":"alpine","Names":"offset","State":"running","Status":"Up"}'
  printf '%s\n' '{"Command":"sleep","CreatedAt":"2024-01-02T01:04:05Z","ID":"zulu","Image":"alpine","Names":"zulu","State":"running","Status":"Up"}'
  exit 0
fi
exit 2
""")));
      var context = new DriverContext("docker");
      driver.Initialize(context);

      var result = await driver.ListAsync(context, cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      Assert.Equal(TimeSpan.FromHours(2), result.Data.Single(c => c.Id == "offset").Created.Offset);
      Assert.Equal(TimeSpan.Zero, result.Data.Single(c => c.Id == "zulu").Created.Offset);
    }

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
      // Wall-clock ceiling (not an iteration count, which thread-pool starvation stretches) —
      // waits for the fake to both start AND write its cid marker under load. See FakeProcessMarker.
      var deadline = DateTime.UtcNow.AddSeconds(30);
      while ((!File.Exists(record) || !File.ReadAllText(record).Contains("cid-written", StringComparison.Ordinal))
             && DateTime.UtcNow <= deadline)
        await Task.Delay(20, TestContext.Current.CancellationToken);
      Assert.True(File.Exists(record), "fake docker did not start");
      cts.Cancel();

      await Assert.ThrowsAsync<OperationCanceledException>(() => run);

      var commands = await File.ReadAllTextAsync(record, TestContext.Current.CancellationToken);
      Assert.Contains("run -d --cidfile", commands, StringComparison.Ordinal);
      Assert.Contains("rm -f detached-123", commands, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetLogsAsync_ChattyOutput_ReturnsMarkedTailInsteadOfFailing()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var driver = new DockerCliContainerDriver(new FakeResolver(CreateFakeDocker("""
#!/bin/sh
head -c 5242880 /dev/zero | tr '\0' x
echo
echo TAIL
exit 0
""")));
      driver.Initialize(new DriverContext("docker"));

      var result = await driver.GetLogsAsync(new DriverContext("docker"), "ctr", cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      Assert.Contains("[FluentDocker: output truncated", result.Data);
      Assert.EndsWith("TAIL\n", result.Data);
    }

    [Fact]
    public async Task RunAsync_ForegroundOutputAndError_AreDelimited()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var driver = new DockerCliContainerDriver(new FakeResolver(CreateFakeDocker("""
#!/bin/sh
echo abc123 > "$3"
printf out
printf err >&2
exit 0
""")));
      driver.Initialize(new DriverContext("docker"));

      var result = await driver.RunAsync(new DriverContext("docker"), new ContainerCreateConfig
      {
        Image = "alpine",
        Detach = false
      }, TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      Assert.Equal("out\nerr", result.Data.Output);
    }

    [Fact]
    public async Task TopAndKill_PositionalsAreSplitAndGuarded()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var record = Path.Combine(TestOutputDirectory(), $"top-args-{Guid.NewGuid():N}.txt");
      var driver = new DockerCliContainerDriver(new FakeResolver(CreateRecordingDocker(record, "PID CMD\n1 sh")));
      driver.Initialize(new DriverContext("docker"));

      var top = await driver.TopAsync(new DriverContext("docker"), "ctr", "aux --sort=-rss", TestContext.Current.CancellationToken);
      var kill = await driver.KillAsync(new DriverContext("docker"), "ctr", "-9", TestContext.Current.CancellationToken);

      Assert.True(top.Success, top.Error);
      Assert.Equal(["top", "ctr", "aux", "--sort=-rss"], await ReadArgsAsync(record));
      Assert.False(kill.Success);
      Assert.Equal(ErrorCodes.General.InvalidArgument, kill.ErrorCode);
    }

    [Fact]
    public async Task TopAsync_PreservesSpacesInLastColumn()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var driver = new DockerCliContainerDriver(new FakeResolver(CreateFakeDocker("""
#!/bin/sh
printf '%s\n' 'PID USER CMD'
printf '%s\n' '1 root nginx: master process nginx -g daemon off;'
""")));
      driver.Initialize(new DriverContext("docker"));

      var result = await driver.TopAsync(new DriverContext("docker"), "ctr", cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      Assert.Equal(["PID", "USER", "CMD"], result.Data.Titles);
      Assert.Equal(["1", "root", "nginx: master process nginx -g daemon off;"], result.Data.Processes[0]);
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

    [Fact]
    public async Task RunAsync_ForegroundNonZeroWorkloadExit_SucceedsAndSurfacesExitCode()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var driver = new DockerCliContainerDriver(new FakeResolver(CreateFakeDocker("""
#!/bin/sh
cid=''
while [ "$#" -gt 0 ]; do
  if [ "$1" = "--cidfile" ]; then cid="$2"; shift 2; continue; fi
  shift
done
echo abc123 > "$cid"
echo stdout
echo stderr 1>&2
exit 3
""")));
      driver.Initialize(new DriverContext("docker"));

      var result = await driver.RunAsync(new DriverContext("docker"), new ContainerCreateConfig
      {
        Image = "alpine",
        Detach = false
      }, TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      Assert.Equal(3, result.Data.ExitCode);
      Assert.Equal("abc123", result.Data.Id);
      Assert.Contains("stdout", result.Data.Output);
      Assert.Contains("stderr", result.Data.Output);
    }

    [Fact]
    public async Task RunAsync_ForegroundPreflightFailureWithoutCidFile_Fails()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var driver = new DockerCliContainerDriver(new FakeResolver(CreateFakeDocker("""
#!/bin/sh
echo 'docker: invalid reference format' 1>&2
exit 125
""")));
      driver.Initialize(new DriverContext("docker"));

      var result = await driver.RunAsync(new DriverContext("docker"), new ContainerCreateConfig
      {
        Image = "Alpine:Bad:Ref",
        Detach = false
      }, TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Container.CreateFailed, result.ErrorCode);
      Assert.Equal(125, result.ExitCode);
    }

    [Fact]
    public async Task RunAsync_ForegroundContainerStderrDaemonText_WithCidFile_Succeeds()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var driver = new DockerCliContainerDriver(new FakeResolver(CreateFakeDocker("""
#!/bin/sh
cid=''
while [ "$#" -gt 0 ]; do
  if [ "$1" = "--cidfile" ]; then cid="$2"; shift 2; continue; fi
  shift
done
echo abc123 > "$cid"
echo 'Error response from daemon: from workload' 1>&2
exit 2
""")));
      driver.Initialize(new DriverContext("docker"));

      var result = await driver.RunAsync(new DriverContext("docker"), new ContainerCreateConfig
      {
        Image = "alpine",
        Detach = false
      }, TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      Assert.Equal(2, result.Data.ExitCode);
      Assert.Equal("abc123", result.Data.Id);
    }

    [Fact]
    public async Task RunAsync_DetachedSuccess_LeavesExitCodeNull()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var driver = new DockerCliContainerDriver(new FakeResolver(CreateFakeDocker("""
#!/bin/sh
echo abc123
exit 0
""")));
      driver.Initialize(new DriverContext("docker"));

      var result = await driver.RunAsync(new DriverContext("docker"), new ContainerCreateConfig
      {
        Image = "alpine",
        Detach = true
      }, TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      Assert.Null(result.Data.ExitCode);
      Assert.Equal("abc123", result.Data.Id);
    }

    [Fact]
    public async Task RunAsync_Cancellation_RemovesRecoveredCidfileContainer()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var record = Path.Combine(TestOutputDirectory(), $"rm-{Guid.NewGuid():N}.txt");
      var ready = Path.Combine(TestOutputDirectory(), $"ready-{Guid.NewGuid():N}.txt");
      var driver = new DockerCliContainerDriver(new FakeResolver(CreateFakeDocker($$"""
#!/bin/sh
if [ "$1" = "rm" ]; then
  printf '%s\n' "$@" > "{{record}}"
  exit 0
fi
cid=''
while [ "$#" -gt 0 ]; do
  if [ "$1" = "--cidfile" ]; then cid="$2"; shift 2; continue; fi
  shift
done
echo leaked123 > "$cid"
echo ready > "{{ready}}"
sleep 30
""")));
      driver.Initialize(new DriverContext("docker"));
      using var cts = new CancellationTokenSource();
      var task = driver.RunAsync(new DriverContext("docker"), new ContainerCreateConfig
      {
        Image = "alpine",
        Detach = false
      }, cts.Token);

      await WaitForFileAsync(ready);
      await cts.CancelAsync();
      await Assert.ThrowsAsync<OperationCanceledException>(() => task);

      Assert.Equal(["rm", "-f", "leaked123"], await ReadArgsAsync(record));
    }

    [Fact]
    public async Task RunAsync_Cancellation_TimeBoxesCleanup()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var ready = Path.Combine(TestOutputDirectory(), $"ready-{Guid.NewGuid():N}.txt");
      var driver = new DockerCliContainerDriver(new FakeResolver(CreateFakeDocker($$"""
#!/bin/sh
if [ "$1" = "rm" ]; then
  sleep 30
  exit 0
fi
cid=''
while [ "$#" -gt 0 ]; do
  if [ "$1" = "--cidfile" ]; then cid="$2"; shift 2; continue; fi
  shift
done
echo leaked123 > "$cid"
echo ready > "{{ready}}"
sleep 30
""")));
      var context = new DriverContext("docker") { RequestTimeout = TimeSpan.FromSeconds(30) };
      driver.Initialize(context);
      using var cts = new CancellationTokenSource();
      var task = driver.RunAsync(context, new ContainerCreateConfig
      {
        Image = "alpine",
        Detach = false
      }, cts.Token);

      await WaitForFileAsync(ready);
      var sw = Stopwatch.StartNew();
      await cts.CancelAsync();

      await Assert.ThrowsAsync<OperationCanceledException>(() => task);

      Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), $"Cancellation cleanup took {sw.Elapsed}.");
    }

    [Fact]
    public async Task HealthCheck_CmdExecFormShellQuotesTokens_ButCmdShellPassesString()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var record = Path.Combine(TestOutputDirectory(), $"args-{Guid.NewGuid():N}.txt");
      var driver = new DockerCliContainerDriver(new FakeResolver(CreateRecordingDocker(record, "container-123")));
      driver.Initialize(new DriverContext("docker"));

      var execForm = await driver.CreateAsync(new DriverContext("docker"), new ContainerCreateConfig
      {
        Image = "alpine",
        HealthCheck = new HealthCheckConfig { Test = ["CMD", "sh", "-c", "wget -qO- http://x | grep ok"] }
      }, TestContext.Current.CancellationToken);
      Assert.True(execForm.Success, execForm.Error);
      var execArgs = await ReadArgsAsync(record);
      Assert.Equal("sh -c 'wget -qO- http://x | grep ok'", execArgs[Array.IndexOf(execArgs, "--health-cmd") + 1]);

      var shellForm = await driver.CreateAsync(new DriverContext("docker"), new ContainerCreateConfig
      {
        Image = "alpine",
        HealthCheck = new HealthCheckConfig { Test = ["CMD-SHELL", "wget -qO- http://x | grep ok"] }
      }, TestContext.Current.CancellationToken);
      Assert.True(shellForm.Success, shellForm.Error);
      var shellArgs = await ReadArgsAsync(record);
      Assert.Equal("wget -qO- http://x | grep ok", shellArgs[Array.IndexOf(shellArgs, "--health-cmd") + 1]);
    }

    [Fact]
    public async Task HealthCheck_CmdExecFormShellQuotesWordStartSpecials()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var record = Path.Combine(TestOutputDirectory(), $"args-{Guid.NewGuid():N}.txt");
      var driver = new DockerCliContainerDriver(new FakeResolver(CreateRecordingDocker(record, "container-123")));
      driver.Initialize(new DriverContext("docker"));

      var result = await driver.CreateAsync(new DriverContext("docker"), new ContainerCreateConfig
      {
        Image = "alpine",
        HealthCheck = new HealthCheckConfig { Test = ["CMD", "#x", "~root/bin/check", "A=B"] }
      }, TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      var args = await ReadArgsAsync(record);
      Assert.Equal("'#x' '~root/bin/check' 'A=B'", args[Array.IndexOf(args, "--health-cmd") + 1]);
    }

    private sealed class TestBinaryResolver(string directory) : IBinaryResolver
    {
      private readonly DockerBinary _binary = new(directory, "docker", SudoMechanism.None, null!);

      public DockerBinary[] Binaries => [_binary];
      public DockerBinary MainDockerClient => _binary;
      public DockerBinary MainDockerCli => _binary;
      public DockerBinary Resolve(string binary) => _binary;
      public string ResolveBinaryPath(string dockerCommand) => _binary.FqPath;
    }
  }
}
