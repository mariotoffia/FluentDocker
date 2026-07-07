using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Cli.Binary;
using FluentDocker.Drivers.Docker.Cli.Components;
using FluentDocker.Model.Common;
using FluentDocker.Model.Drivers;
using Microsoft.Extensions.Logging;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Docker
{
  [Trait("Category", "Unit")]
  [Trait("Requires", "PosixShell")]
  public sealed partial class DockerCliProductionReadinessTests
  {
    [Fact]
    public async Task DockerCliJsonFixtures_ParseRealCreatedAtFormats()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      // Fixture fidelity: captured from Docker 29.5.3 on 2026-07-03 with
      // `docker ps/images/history --format "{{json .}}"`; tests never require Docker.
      var driver = CreateFakeDocker("""
#!/bin/sh
case "$1" in
  ps) printf '%s\n' '{"Command":"\"sleep 300\"","CreatedAt":"2026-07-03 15:07:49 +0200 CEST","ID":"f33a5931b70c","Image":"alpine:latest","Names":"fdfixture","State":"running","Status":"Up Less than a second"}'; exit 0 ;;
  images) printf '%s\n' '{"Containers":"0","CreatedAt":"2026-06-22 22:53:00 +0200 CEST","ID":"54f2a904c251","Repository":"nginx","Size":"92.6MB","Tag":"alpine"}'; exit 0 ;;
  history) printf '%s\n' '{"Comment":"buildkit.dockerfile.v0","CreatedAt":"2026-06-16T02:01:20+02:00","CreatedBy":"CMD [\"/bin/sh\"]","ID":"28bd5fe8b56d","Size":"0B"}'; exit 0 ;;
esac
exit 2
""");
      var container = new DockerCliContainerDriver(new FakeResolver(driver));
      var image = new DockerCliImageDriver(new FakeResolver(driver));
      container.Initialize(new DriverContext("docker"));
      image.Initialize(new DriverContext("docker"));

      var containers = await container.ListAsync(new DriverContext("docker"), cancellationToken: TestContext.Current.CancellationToken);
      var images = await image.ListAsync(new DriverContext("docker"), cancellationToken: TestContext.Current.CancellationToken);
      var history = await image.HistoryAsync(new DriverContext("docker"), "alpine:latest", TestContext.Current.CancellationToken);

      Assert.True(containers.Success, containers.Error);
      Assert.True(images.Success, images.Error);
      Assert.True(history.Success, history.Error);
      // Container keeps the engine offset; image and history still expose UTC DateTime.
      Assert.Equal(new DateTimeOffset(2026, 7, 3, 15, 7, 49, TimeSpan.FromHours(2)), Assert.Single(containers.Data).Created);
      Assert.Equal(new DateTime(2026, 6, 22, 20, 53, 0, DateTimeKind.Utc), Assert.Single(images.Data).Created);
      Assert.Equal(new DateTime(2026, 6, 16, 0, 1, 20, DateTimeKind.Utc), Assert.Single(history.Data).Created);
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
    public async Task LoginAsync_BrokenStdinPipe_PreservesDockerStderrAndExitCode()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var driver = new DockerCliAuthDriver(new FakeResolver(CreateFakeDocker("""
#!/bin/sh
echo 'registry rejected token' >&2
exit 9
""")));
      driver.Initialize(new DriverContext("docker"));

      var result = await driver.LoginAsync(
          new DriverContext("docker"),
          new RegistryLoginConfig
          {
            Username = "user",
            Password = new string('å', 5 * 1024 * 1024),
            PasswordStdin = true
          },
          TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(9, result.ExitCode);
      Assert.Contains("registry rejected token", result.Error);
      Assert.DoesNotContain("Broken pipe", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoginAsync_PasswordStdin_UsesUtf8Encoding()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var original = Console.InputEncoding;
      try
      {
        Console.InputEncoding = Encoding.Latin1;
        var driver = new DockerCliAuthDriver(new FakeResolver(CreateFakeDocker("""
#!/bin/sh
hex=$(od -An -tx1 | tr -d ' \n')
[ "$hex" = "70c3a47373" ] || { echo "$hex" >&2; exit 8; }
exit 0
""")));
        driver.Initialize(new DriverContext("docker"));

        var result = await driver.LoginAsync(
            new DriverContext("docker"),
            new RegistryLoginConfig { Username = "user", Password = "päss", PasswordStdin = true },
            TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
      }
      finally
      {
        Console.InputEncoding = original;
      }
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
    public async Task SudoCommand_QuotesDockerBinaryPath()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var dir = TestOutputDirectory();
      var sudoDir = Path.Combine(dir, $"sudo-{Guid.NewGuid():N}");
      var dockerDir = Path.Combine(dir, $"docker bin {Guid.NewGuid():N}");
      Directory.CreateDirectory(sudoDir);
      Directory.CreateDirectory(dockerDir);
      var record = Path.Combine(dir, $"sudo-args-{Guid.NewGuid():N}.txt");
      WriteExecutable(Path.Combine(sudoDir, "sudo"), $"""
#!/bin/sh
printf '%s\n' "$@" > '{record}'
exit 0
""");
      var dockerPath = Path.Combine(dockerDir, "docker");
      WriteExecutable(dockerPath, "#!/bin/sh\nexit 0\n");
      var oldPath = Environment.GetEnvironmentVariable("PATH");
      try
      {
        Environment.SetEnvironmentVariable("PATH", $"{sudoDir}:{oldPath}");
        var driver = new DockerCliContainerDriver(new SudoResolver(dockerPath));
        driver.Initialize(new DriverContext("docker"));

        var result = await driver.StartAsync(
            new DriverContext("docker") { Sudo = SudoMechanism.NoPassword },
            "ctr",
            TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        Assert.Equal(["--", dockerPath, "start", "ctr"], await ReadArgsAsync(record));
      }
      finally
      {
        Environment.SetEnvironmentVariable("PATH", oldPath);
      }
    }

    [Fact]
    public async Task LeadingDashPositionals_AreRejectedAcrossDockerCliDrivers()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var marker = Path.Combine(TestOutputDirectory(), $"dash-marker-{Guid.NewGuid():N}");
      var docker = CreateFakeDocker($"""
#!/bin/sh
touch '{marker}'
exit 0
""");
      var context = new DriverContext("docker");
      var image = new DockerCliImageDriver(new FakeResolver(docker));
      var compose = new DockerCliComposeDriver(new FakeResolver(docker));
      var stack = new DockerCliStackDriver(new FakeResolver(docker));
      var auth = new DockerCliAuthDriver(new FakeResolver(docker));
      image.Initialize(context);
      compose.Initialize(context);
      stack.Initialize(context);
      auth.Initialize(context);

      var failures = new List<(bool Success, string ErrorCode)>
      {
        ToStatus(await image.PullAsync(context, "-repo", "", cancellationToken: TestContext.Current.CancellationToken)),
        ToStatus(await image.PushAsync(context, "-repo", cancellationToken: TestContext.Current.CancellationToken)),
        ToStatus(await image.SaveAsync(context, ["-repo"], "out.tar", TestContext.Current.CancellationToken)),
        ToStatus(await compose.StartAsync(context, new ComposeFileConfig { Services = ["-svc"] }, TestContext.Current.CancellationToken)),
        ToStatus(await stack.DeployAsync(context, new StackDeployConfig { StackName = "-stack" }, TestContext.Current.CancellationToken)),
        ToStatus(await stack.RemoveAsync(context, ["-stack"], TestContext.Current.CancellationToken)),
        ToStatus(await auth.LoginAsync(context, new RegistryLoginConfig { Server = "-registry" }, TestContext.Current.CancellationToken)),
        ToStatus(await auth.LogoutAsync(context, "-registry", TestContext.Current.CancellationToken))
      };

      Assert.All(failures, failure =>
      {
        Assert.False(failure.Success);
        Assert.Equal(ErrorCodes.General.InvalidArgument, failure.ErrorCode);
      });
      Assert.False(File.Exists(marker));
    }

    [Fact]
    public async Task StackPs_NoTrunc_DoesNotRewriteStackName()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var record = Path.Combine(TestOutputDirectory(), $"stack-args-{Guid.NewGuid():N}.txt");
      var driver = new DockerCliStackDriver(new FakeResolver(CreateRecordingDocker(record, "")));
      driver.Initialize(new DriverContext("docker"));

      var result = await driver.GetTasksAsync(
          new DriverContext("docker"),
          "my stack ps",
          new StackTaskFilter { NoTrunc = true },
          TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      Assert.Equal("my stack ps", (await ReadArgsAsync(record)).Last());
    }

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
    public async Task StreamLogsAsync_FiltersAndMarksStderr()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var driver = new DockerCliStreamDriver(new FakeResolver(CreateFakeDocker("""
#!/bin/sh
echo out
echo err >&2
exit 0
""")));
      driver.Initialize(new DriverContext("docker"));

      var stdoutOnly = new List<string>();
      await foreach (var line in driver.StreamLogsAsync(
          new DriverContext("docker"),
          "ctr",
          new StreamLogsConfig { Follow = false, Stderr = false },
          TestContext.Current.CancellationToken))
        stdoutOnly.Add(line);

      var entries = new List<LogEntry>();
      await foreach (var entry in ((IStreamDriver)driver).StreamLogEntriesAsync(
          new DriverContext("docker"),
          "ctr",
          new StreamLogsConfig { Follow = false },
          TestContext.Current.CancellationToken))
        entries.Add(entry);

      Assert.Equal(["out"], stdoutOnly);
      Assert.Contains(entries, e => e.Source == LogStreamSource.Stdout && e.Line == "out");
      Assert.Contains(entries, e => e.Source == LogStreamSource.Stderr && e.Line == "err");
    }

    [Fact]
    public async Task AttachAsync_HonorsStdinAndRejectsUnsupportedStreamSuppression()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var record = Path.Combine(TestOutputDirectory(), $"attach-args-{Guid.NewGuid():N}.txt");
      var driver = new DockerCliStreamDriver(new FakeResolver(CreateFakeDocker($"""
#!/bin/sh
printf '%s\n' "$@" > '{record}'
sleep 2
""")));
      driver.Initialize(new DriverContext("docker"));

      await using var attach = (await driver.AttachAsync(
          new DriverContext("docker"),
          "ctr",
          new AttachConfig { Stdin = false },
          TestContext.Current.CancellationToken)).Data;
      await WaitForFileAsync(record);
      Assert.Contains("--no-stdin", await File.ReadAllTextAsync(record, TestContext.Current.CancellationToken));

      var unsupported = await driver.AttachAsync(
          new DriverContext("docker"),
          "ctr",
          new AttachConfig { NoStdout = true },
          TestContext.Current.CancellationToken);
      Assert.False(unsupported.Success);
      Assert.Equal(ErrorCodes.General.InvalidArgument, unsupported.ErrorCode);
    }

    [Fact]
    public async Task VolumeSystemAndImageFindings_ReturnProductionReadyResults()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var logs = new List<string>();
      var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(new ListLoggerProvider(logs)));
      var driver = CreateFakeDocker("""
#!/bin/sh
case "$1 $2" in
  "volume inspect") echo '[]'; exit 0 ;;
  "version --format") echo 'daemon down' >&2; exit 7 ;;
  "system df") printf '%s\n' '{"Type":'; exit 0 ;;
  "images --format") printf '%s\n' '{"ID":"sha256:abc","Repository":"repo","Tag":"latest","Size":"1KiB","CreatedAt":"2026-06-22 22:53:00 +0200 CEST"}'; exit 0 ;;
  "history --format") printf '%s\n' '{"ID":"layer","CreatedAt":"2026-06-16T02:01:20+02:00","CreatedBy":"CMD","Size":"2MiB"}'; exit 0 ;;
esac
exit 2
""");
      var context = new DriverContext("docker") { LoggerFactory = loggerFactory };
      var volume = new DockerCliVolumeDriver(new FakeResolver(driver));
      var system = new DockerCliSystemDriver(new FakeResolver(driver));
      var image = new DockerCliImageDriver(new FakeResolver(driver));
      volume.Initialize(context);
      system.Initialize(context);
      image.Initialize(context);

      var missing = await volume.InspectAsync(context, "missing", TestContext.Current.CancellationToken);
      var linux = await system.IsLinuxEngineAsync(context, TestContext.Current.CancellationToken);
      var disk = await system.GetDiskUsageAsync(context, TestContext.Current.CancellationToken);
      var images = await image.ListAsync(context, cancellationToken: TestContext.Current.CancellationToken);
      var history = await image.HistoryAsync(context, "repo:latest", TestContext.Current.CancellationToken);

      Assert.False(missing.Success);
      Assert.Equal(ErrorCodes.Volume.NotFound, missing.ErrorCode);
      Assert.False(linux.Success);
      Assert.Equal(7, linux.ExitCode);
      Assert.True(disk.Success, disk.Error);
      Assert.Contains(logs, l => l.Contains("Disk usage JSON parsing failed", StringComparison.Ordinal));
      Assert.Equal(1024, Assert.Single(images.Data).Size);
      Assert.Equal(2 * 1024 * 1024, Assert.Single(history.Data).Size);
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

    private static void WriteExecutable(string path, string script)
    {
      File.WriteAllText(path, script.Replace("\r\n", "\n"));
      if (!OperatingSystem.IsWindows())
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private static (bool Success, string ErrorCode) ToStatus<T>(CommandResponse<T> response) =>
        (response.Success, response.ErrorCode);

    private static async Task WaitForFileAsync(string path)
    {
      // Budget generous enough to survive process-spawn latency under a fully
      // parallel unit run (fake docker writes args, then sleeps 2s, so the file
      // persists well past the poll window once the child has started).
      for (var i = 0; i < 250; i++)
      {
        if (File.Exists(path))
          return;
        await Task.Delay(20, TestContext.Current.CancellationToken);
      }
    }

    private sealed class SudoResolver(string dockerPath) : IBinaryResolver
    {
      private readonly DockerBinary _binary = new(
          Path.GetDirectoryName(dockerPath) ?? ".",
          Path.GetFileName(dockerPath),
          SudoMechanism.NoPassword,
          null!,
          DockerBinaryType.DockerClient);

      public DockerBinary[] Binaries => [_binary];
      public DockerBinary MainDockerClient => _binary;
      public DockerBinary MainDockerCompose => _binary;
      public DockerBinary MainDockerCli => _binary;
      public DockerBinary Resolve(string binary) => _binary;
      public string ResolveBinaryPath(string dockerCommand) => _binary.FqPath;
    }

    private sealed class ListLoggerProvider(List<string> messages) : ILoggerProvider
    {
      public ILogger CreateLogger(string categoryName) => new ListLogger(messages);
      public void Dispose() { }
    }

    private sealed class ListLogger(List<string> messages) : ILogger
    {
      public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
      public bool IsEnabled(LogLevel logLevel) => true;
      public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
          messages.Add(formatter(state, exception));
    }

    private sealed class NullScope : IDisposable
    {
      public static readonly NullScope Instance = new();
      public void Dispose() { }
    }
  }
}
