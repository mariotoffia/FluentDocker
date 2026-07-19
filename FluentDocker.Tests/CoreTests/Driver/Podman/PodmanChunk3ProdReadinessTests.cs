using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Podman;
using FluentDocker.Drivers.Podman.Cli;
using FluentDocker.Drivers.Podman.Cli.Binary;
using FluentDocker.Drivers.Podman.Cli.Components;
using FluentDocker.Model.Common;
using FluentDocker.Model.Drivers;
using Microsoft.Extensions.Logging;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Podman
{
  [Trait("Category", "Unit")]
  [Trait("Requires", "PosixShell")]
  public class PodmanChunk3ProdReadinessTests
  {
    [Fact]
    public async Task RunAsync_DetachedCancellation_RemovesCidFileContainer()
    {
      RequirePosixShellFixture();
      var scratch = CreateOutputDirectory("podman-chunk3-detached-cancel");
      var record = Path.Combine(scratch, "rm.txt");
      var started = Path.Combine(scratch, "started.txt");
      var originalTempPath = DirectoryHelper.GetTempPath;
      DirectoryHelper.GetTempPath = () => scratch;
      try
      {
        var driver = CreateContainerDriver(Return($"""
            if [ "$1" = 'rm' ]; then
              printf '%s\n' "$@" > '{record}'
              exit 0
            fi
            cid=''
            while [ "$#" -gt 0 ]; do
              if [ "$1" = '--cidfile' ]; then shift; cid="$1"; fi
              shift
            done
            [ -n "$cid" ] && printf '%s' 'ctr-detached' > "$cid"
            : > '{started}'
            while :; do sleep 1; done
            """));
        using var cts = new CancellationTokenSource();
        var run = driver.RunAsync(
            new DriverContext("podman"),
            new ContainerCreateConfig { Image = "alpine", Detach = true },
            cts.Token);

        await WaitForFileAsync(started);
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() => run);
        Assert.Equal("rm\n-f\nctr-detached\n", await File.ReadAllTextAsync(record, TestContext.Current.CancellationToken));
      }
      finally
      {
        DirectoryHelper.GetTempPath = originalTempPath;
      }
    }

    [Fact]
    public async Task SudoNoPassword_UsesNonInteractiveFlag()
    {
      RequirePosixShellFixture();
      var scratch = CreateOutputDirectory("podman-chunk3-sudo-n");
      var record = Path.Combine(scratch, "sudo-args.txt");
      WriteExecutable(Path.Combine(scratch, "sudo"), $"""
          #!/bin/sh
          printf '%s\n' "$@" > '{record}'
          exit 0
          """);
      var oldPath = Environment.GetEnvironmentVariable("PATH");
      Environment.SetEnvironmentVariable("PATH", scratch + Path.PathSeparator + oldPath);
      try
      {
        var driver = CreateSystemDriver(scratch, SudoMechanism.NoPassword);

        var result = await driver.GetInfoAsync(new DriverContext("podman"), TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        var args = await File.ReadAllLinesAsync(record, TestContext.Current.CancellationToken);
        Assert.Contains("-n", args);
        Assert.Contains("--", args);
      }
      finally
      {
        Environment.SetEnvironmentVariable("PATH", oldPath);
      }
    }

    [Fact]
    public async Task RunAsync_NameStartingWithDash_FailsBeforePodmanStarts()
    {
      RequirePosixShellFixture();
      var scratch = CreateOutputDirectory("podman-chunk3-leading-name");
      var started = Path.Combine(scratch, "started.txt");
      var driver = CreateContainerDriver(Return($"""
          : > '{started}'
          echo 'ctr'
          """));

      var result = await driver.RunAsync(
          new DriverContext("podman"),
          new ContainerCreateConfig { Image = "alpine", Name = "-web", Detach = true },
          TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.General.InvalidArgument, result.ErrorCode);
      Assert.False(File.Exists(started));
    }

    [Fact]
    public async Task PullPushBuildAsync_ReportProgressLines()
    {
      RequirePosixShellFixture();
      var driver = CreateImageDriver(Return("""
          if [ "$1" = 'build' ]; then
            iid=''
            while [ "$#" -gt 0 ]; do
              if [ "$1" = '--iidfile' ]; then shift; iid="$1"; fi
              shift
            done
            [ -n "$iid" ] && printf '%s' 'sha256:built' > "$iid"
            echo 'build-step'
            exit 0
          fi
          echo "$1-step"
          exit 0
          """));
      var pull = new RecordingProgress<ImagePullProgress>();
      var push = new RecordingProgress<ImagePushProgress>();
      var build = new RecordingProgress<ImageBuildProgress>();

      var pullResult = await driver.PullAsync(new DriverContext("podman"), "alpine", progress: pull, cancellationToken: TestContext.Current.CancellationToken);
      var pushResult = await driver.PushAsync(new DriverContext("podman"), "alpine", push, TestContext.Current.CancellationToken);
      var buildResult = await driver.BuildAsync(new DriverContext("podman"), new ImageBuildConfig { BuildContext = "." }, build, TestContext.Current.CancellationToken);

      Assert.True(pullResult.Success, pullResult.Error);
      Assert.True(pushResult.Success, pushResult.Error);
      Assert.True(buildResult.Success, buildResult.Error);
      Assert.Contains(pull.Items, p => p.Status == "pull-step");
      Assert.Contains(push.Items, p => p.Status == "push-step");
      Assert.Contains(build.Items, p => p.Status == "build-step" && p.Stream == "build-step");
    }

    [Fact]
    public void MachineLocks_AreCaseInsensitive()
    {
      var field = typeof(PodmanCliDriverPack).GetField("MachineLocks", BindingFlags.NonPublic | BindingFlags.Static);
      var locks = Assert.IsType<ConcurrentDictionary<string, SemaphoreSlim>>(field?.GetValue(null));

      Assert.True(locks.Comparer.Equals("Default", "default"));
    }

    [Fact]
    public async Task BuildAsync_CanceledBuild_DeletesIidFile()
    {
      RequirePosixShellFixture();
      var scratch = CreateOutputDirectory("podman-chunk3-iid-cancel");
      var iidRecord = Path.Combine(scratch, "iid-path.txt");
      var originalTempPath = DirectoryHelper.GetTempPath;
      DirectoryHelper.GetTempPath = () => scratch;
      try
      {
        var driver = CreateImageDriver(Return($"""
            iid=''
            while [ "$#" -gt 0 ]; do
              if [ "$1" = '--iidfile' ]; then shift; iid="$1"; fi
              shift
            done
            printf '%s' "$iid" > '{iidRecord}'
            printf '%s' 'sha256:cancelled' > "$iid"
            while :; do sleep 1; done
            """));
        using var cts = new CancellationTokenSource();
        var build = driver.BuildAsync(
            new DriverContext("podman"),
            new ImageBuildConfig { BuildContext = "." },
            cancellationToken: cts.Token);

        await WaitForFileAsync(iidRecord);
        var iidPath = await File.ReadAllTextAsync(iidRecord, TestContext.Current.CancellationToken);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => build);
        Assert.False(File.Exists(iidPath));
      }
      finally
      {
        DirectoryHelper.GetTempPath = originalTempPath;
      }
    }

    [Fact]
    public async Task StreamLogEntriesAsync_DetailsTrue_LogsWarningOnceAndOmitsFlag()
    {
      RequirePosixShellFixture();
      var loggerProvider = new RecordingLoggerProvider();
      using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(loggerProvider));
      var scratch = CreateOutputDirectory("podman-chunk3-details-warning");
      var record = Path.Combine(scratch, "args.txt");
      WriteExecutable(Path.Combine(scratch, "podman"), $"""
          #!/bin/sh
          printf '%s\n' "$@" >> '{record}'
          exit 0
          """);
      var driver = CreateStreamDriverFromDirectory(scratch);
      var context = new DriverContext("podman") { LoggerFactory = loggerFactory };
      driver.Initialize(context);
      var config = new StreamLogsConfig { Details = true, Follow = false };

      await DrainAsync(driver.StreamLogEntriesAsync(context, "ctr", config, TestContext.Current.CancellationToken));
      await DrainAsync(driver.StreamLogEntriesAsync(context, "ctr", config, TestContext.Current.CancellationToken));

      Assert.Single(loggerProvider.Warnings, w => w.Contains("Details", StringComparison.OrdinalIgnoreCase));
      var args = await File.ReadAllTextAsync(record, TestContext.Current.CancellationToken);
      Assert.DoesNotContain("--details", args);
    }

    [Fact]
    public async Task MissingPodmanBinary_MapsStreamingUnboundedAndAttachStartsToDriverException()
    {
      RequirePosixShellFixture();
      var missingDir = CreateOutputDirectory("podman-chunk3-missing-binary");
      var missingPath = Path.Combine(missingDir, "podman");
      var resolver = new PodmanResolver(missingDir, SudoMechanism.None);
      var stream = new PodmanCliStreamDriver(resolver);
      stream.Initialize(new DriverContext("podman"));
      var container = new PodmanCliContainerDriver(resolver);
      container.Initialize(new DriverContext("podman"));

      var streamingError = await Assert.ThrowsAsync<DriverException>(async () =>
          await DrainAsync(stream.StreamEventsAsync(new DriverContext("podman"), cancellationToken: TestContext.Current.CancellationToken)));
      var logs = await container.GetLogsAsync(new DriverContext("podman"), "ctr", cancellationToken: TestContext.Current.CancellationToken);
      var attach = await stream.AttachAsync(new DriverContext("podman"), "ctr", cancellationToken: TestContext.Current.CancellationToken);

      Assert.Contains(missingPath, streamingError.Message);
      Assert.False(logs.Success);
      Assert.Equal(ErrorCodes.Driver.CommandExecutionFailed, logs.ErrorCode);
      Assert.Contains(missingPath, logs.Error);
      Assert.False(attach.Success);
      Assert.Equal(ErrorCodes.Driver.CommandExecutionFailed, attach.ErrorCode);
      Assert.Contains(missingPath, attach.Error);
    }

    [Fact]
    public async Task GetLogsAsync_DocumentsMergedRollingTailBehavior()
    {
      RequirePosixShellFixture();
      var driver = CreateContainerDriver(Return("""
          echo 'stdout-first'
          echo 'stderr-second' >&2
          echo 'stdout-third'
          echo 'stderr-fourth' >&2
          """));

      var result = await driver.GetLogsAsync(new DriverContext("podman"), "ctr", cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      Assert.Equal("stdout-first\nstdout-third\nstderr-second\nstderr-fourth", result.Data);
    }

    [Fact]
    public async Task InitAsync_ImageUsesDocumentedPodman5Flag()
    {
      RequirePosixShellFixture();
      var scratch = CreateOutputDirectory("podman-chunk3-machine-image-doc");
      var record = Path.Combine(scratch, "args.txt");
      WriteExecutable(Path.Combine(scratch, "podman"), $"""
          #!/bin/sh
          printf '%s\n' "$@" > '{record}'
          exit 0
          """);
      var driver = CreateMachineDriverFromDirectory(scratch);

      var result = await driver.InitAsync(
          new DriverContext("podman"),
          new MachineInitConfig { Image = "file:///vm.img" },
          TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      var args = await File.ReadAllLinesAsync(record, TestContext.Current.CancellationToken);
      Assert.Contains("--image", args);
      Assert.DoesNotContain("--image-path", args);
    }

    private static async Task DrainAsync<T>(IAsyncEnumerable<T> source)
    {
      await foreach (var _ in source.WithCancellation(TestContext.Current.CancellationToken).ConfigureAwait(false))
      {
      }
    }

    private static Task WaitForFileAsync(string path)
        => FakeProcessMarker.WaitForFileAsync(path, TestContext.Current.CancellationToken);

    private static PodmanCliContainerDriver CreateContainerDriver(string script)
        => CreateContainerDriverFromDirectory(CreatePodmanDirectory("podman-chunk3-container", script));

    private static PodmanCliContainerDriver CreateContainerDriverFromDirectory(string directory)
    {
      var driver = new PodmanCliContainerDriver(new PodmanResolver(directory, SudoMechanism.None));
      driver.Initialize(new DriverContext("podman"));
      return driver;
    }

    private static PodmanCliImageDriver CreateImageDriver(string script)
    {
      var driver = new PodmanCliImageDriver(new PodmanResolver(CreatePodmanDirectory("podman-chunk3-image", script), SudoMechanism.None));
      driver.Initialize(new DriverContext("podman"));
      return driver;
    }

    private static PodmanCliMachineDriver CreateMachineDriverFromDirectory(string directory)
    {
      var driver = new PodmanCliMachineDriver(new PodmanResolver(directory, SudoMechanism.None));
      driver.Initialize(new DriverContext("podman"));
      return driver;
    }

    private static PodmanCliStreamDriver CreateStreamDriverFromDirectory(string directory)
    {
      var driver = new PodmanCliStreamDriver(new PodmanResolver(directory, SudoMechanism.None));
      driver.Initialize(new DriverContext("podman"));
      return driver;
    }

    private static PodmanCliSystemDriver CreateSystemDriver(string directory, SudoMechanism sudo)
    {
      var driver = new PodmanCliSystemDriver(new PodmanResolver(directory, sudo));
      driver.Initialize(new DriverContext("podman"));
      return driver;
    }

    private static string Return(string body) => $"""
        #!/bin/sh
        {body}
        """;

    private static string CreatePodmanDirectory(string name, string script)
    {
      var dir = CreateOutputDirectory(name);
      WriteExecutable(Path.Combine(dir, "podman"), script);
      return dir;
    }

    private static string CreateOutputDirectory(string name)
    {
      var dir = Path.Combine(AppContext.BaseDirectory, ".out", name, Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(dir);
      return dir;
    }

    private static void WriteExecutable(string path, string script)
    {
      File.WriteAllText(path, script.Replace("\r\n", "\n"));
      if (!OperatingSystem.IsWindows())
        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private static void RequirePosixShellFixture()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fixture; not applicable on Windows");
    }

    private sealed class RecordingProgress<T> : IProgress<T>
    {
      public List<T> Items { get; } = [];
      public void Report(T value) => Items.Add(value);
    }

    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
      public List<string> Warnings { get; } = [];
      public ILogger CreateLogger(string categoryName) => new RecordingLogger(Warnings);
      public void Dispose()
      {
      }
    }

    private sealed class RecordingLogger(List<string> warnings) : ILogger
    {
      public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
      public bool IsEnabled(LogLevel logLevel) => true;
      public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
      {
        if (logLevel == LogLevel.Warning)
          warnings.Add(formatter(state, exception));
      }
    }

    private sealed class NullScope : IDisposable
    {
      public static readonly NullScope Instance = new();
      public void Dispose()
      {
      }
    }

    private sealed class PodmanResolver(string directory, SudoMechanism sudo) : IPodmanBinaryResolver
    {
      private readonly PodmanBinary _binary = new(directory, "podman", sudo, null!, PodmanBinaryType.PodmanClient);
      public PodmanBinary[] Binaries => [_binary];
      public PodmanBinary MainPodmanClient => _binary;
      public PodmanBinary PodmanRemote => _binary;
      public PodmanBinary Resolve(string binary) => _binary;
      public string ResolveBinaryPath(string podmanCommand) => _binary.FqPath;
    }
  }
}
