using System;
using System.Collections.Generic;
using System.Diagnostics;
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

      await WaitForReadyFileAsync(ready);
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

      await WaitForReadyFileAsync(ready);
      var sw = Stopwatch.StartNew();
      await cts.CancelAsync();

      await Assert.ThrowsAsync<OperationCanceledException>(() => task);

      Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), $"Cancellation cleanup took {sw.Elapsed}.");
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

    [Fact]
    public async Task ContainerAndImageList_AllJsonLinesFail_ReturnFailure()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var docker = CreateFakeDocker("""
#!/bin/sh
echo not-json
exit 0
""");
      var container = new DockerCliContainerDriver(new FakeResolver(docker));
      var image = new DockerCliImageDriver(new FakeResolver(docker));
      container.Initialize(new DriverContext("docker"));
      image.Initialize(new DriverContext("docker"));

      var containers = await container.ListAsync(new DriverContext("docker"), cancellationToken: TestContext.Current.CancellationToken);
      var images = await image.ListAsync(new DriverContext("docker"), cancellationToken: TestContext.Current.CancellationToken);

      Assert.False(containers.Success);
      Assert.False(images.Success);
    }

    [Fact]
    public async Task ImageHistory_AllJsonLinesFail_ReturnsFailure_EmptyOutputReturnsEmpty()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var invalid = new DockerCliImageDriver(new FakeResolver(CreateFakeDocker("""
#!/bin/sh
echo not-json
exit 0
""")));
      invalid.Initialize(new DriverContext("docker"));

      var invalidResult = await invalid.HistoryAsync(new DriverContext("docker"), "alpine", TestContext.Current.CancellationToken);
      Assert.False(invalidResult.Success);

      var empty = new DockerCliImageDriver(new FakeResolver(CreateFakeDocker("""
#!/bin/sh
exit 0
""")));
      empty.Initialize(new DriverContext("docker"));

      var emptyResult = await empty.HistoryAsync(new DriverContext("docker"), "alpine", TestContext.Current.CancellationToken);
      Assert.True(emptyResult.Success, emptyResult.Error);
      Assert.Empty(emptyResult.Data);
    }

    [Fact]
    public async Task LeadingDashServiceScaleTagAndImportPositionals_FailBeforeDocker()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var docker = CreateFakeDocker("""
#!/bin/sh
echo should-not-run
exit 0
""");
      var service = new DockerCliServiceDriver(new FakeResolver(docker));
      var image = new DockerCliImageDriver(new FakeResolver(docker));
      service.Initialize(new DriverContext("docker"));
      image.Initialize(new DriverContext("docker"));

      var scale = await service.ScaleAsync(new DriverContext("docker"), new Dictionary<string, int> { { "-d", 3 } }, cancellationToken: TestContext.Current.CancellationToken);
      var tag = await image.TagAsync(new DriverContext("docker"), "sha256:abc", "-repo", "latest", TestContext.Current.CancellationToken);
      var import = await image.ImportAsync(new DriverContext("docker"), "-archive.tar", cancellationToken: TestContext.Current.CancellationToken);

      Assert.False(scale.Success);
      Assert.Equal(ErrorCodes.General.InvalidArgument, scale.ErrorCode);
      Assert.False(tag.Success);
      Assert.Equal(ErrorCodes.General.InvalidArgument, tag.ErrorCode);
      Assert.False(import.Success);
      Assert.Equal(ErrorCodes.General.InvalidArgument, import.ErrorCode);
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

    private static async Task WaitForReadyFileAsync(string path)
    {
      for (var i = 0; i < 100; i++)
      {
        if (File.Exists(path))
          return;
        await Task.Delay(50, TestContext.Current.CancellationToken).ConfigureAwait(false);
      }

      throw new TimeoutException($"Timed out waiting for {path}.");
    }
  }
}
