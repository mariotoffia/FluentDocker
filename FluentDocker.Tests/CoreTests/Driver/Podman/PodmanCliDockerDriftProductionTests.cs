using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Podman.Cli.Binary;
using FluentDocker.Drivers.Podman.Cli.Components;
using FluentDocker.Model.Common;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Podman
{
  [Trait("Category", "Unit")]
  [Trait("Requires", "PosixShell")]
  public class PodmanCliDockerDriftProductionTests
  {
    [Fact]
    public async Task RunAsync_ForegroundNonZeroContainerExit_ReturnsOkWithExitCodeAndCid()
    {
      RequirePosixShellFixture();
      var driver = CreateContainerDriver(Return("""
          cid=''
          while [ "$#" -gt 0 ]; do
            if [ "$1" = '--cidfile' ]; then shift; cid="$1"; fi
            shift
          done
          [ -n "$cid" ] && printf '%s' 'ctr-exited' > "$cid"
          printf '%s' 'stdout-line'
          printf '%s' 'stderr-line' >&2
          exit 7
          """));

      var result = await driver.RunAsync(
          new DriverContext("podman"),
          new ContainerCreateConfig { Image = "alpine", Detach = false, Command = ["sh", "-c", "exit 7"] },
          TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      Assert.Equal("ctr-exited", result.Data.Id);
      Assert.Equal(7, result.Data.ExitCode);
      Assert.Equal("stdout-line\nstderr-line", result.Data.Output);
    }

    [Fact]
    public async Task RunAsync_CancelledForegroundRun_RemovesCidFileContainerBeforeRethrow()
    {
      RequirePosixShellFixture();
      var dir = CreateOutputDirectory("podman-run-cancel-cleanup");
      var record = Path.Combine(dir, "args.txt");
      WriteExecutable(Path.Combine(dir, "podman"), $"""
          #!/bin/sh
          printf '%s\n' "$@" >> '{record}'
          if [ "$1" = 'run' ]; then
            cid=''
            while [ "$#" -gt 0 ]; do
              if [ "$1" = '--cidfile' ]; then shift; cid="$1"; fi
              shift
            done
            [ -n "$cid" ] && printf '%s' 'ctr-cancelled' > "$cid"
            sleep 30
          fi
          exit 0
          """);
      var driver = CreateContainerDriverFromDirectory(dir);
      using var cts = new CancellationTokenSource();
      var runTask = driver.RunAsync(
          new DriverContext("podman"),
          new ContainerCreateConfig { Image = "alpine", Detach = false, Command = ["sleep", "30"] },
          cts.Token);

      await ReadLinesEventuallyAsync(record);
      cts.Cancel();
      await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runTask);

      var args = await ReadLinesEventuallyAsync(record);
      Assert.Contains("rm", args);
      Assert.Contains("-f", args);
      Assert.Contains("ctr-cancelled", args);
    }

    [Fact]
    public async Task GetLogsAsync_LogsOverBufferedCap_ReturnsTruncatedTail()
    {
      RequirePosixShellFixture();
      var driver = CreateContainerDriver(Return("""
          yes padding | head -n 700000
          printf '%s' 'DONE_MARKER'
          """));

      var result = await driver.GetLogsAsync(
          new DriverContext("podman"), "ctr", cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      Assert.StartsWith("[FluentDocker: output truncated, showing last ", result.Data);
      Assert.Contains("DONE_MARKER", result.Data);
      Assert.True(result.Data.Length < 4 * 1024 * 1024);
    }

    [Fact]
    public async Task WaitAsync_ParsesExitCodeWithInvariantCulture()
    {
      RequirePosixShellFixture();
      var originalCulture = CultureInfo.CurrentCulture;
      var customCulture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
      customCulture.NumberFormat.NegativeSign = "~";
      CultureInfo.CurrentCulture = customCulture;
      try
      {
        var driver = CreateContainerDriver(Return("echo '-1'"));

        var result = await driver.WaitAsync(
            new DriverContext("podman"), "ctr", TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        Assert.Equal(-1, result.Data.ExitCode);
      }
      finally
      {
        CultureInfo.CurrentCulture = originalCulture;
      }
    }

    [Fact]
    public void ParseTopOutput_CommandColumnWithSpaces_StaysInLastColumn()
    {
      var result = InvokeParseTopOutput("USER PID COMMAND\nroot 1 /bin/sh -c echo hi\n");

      Assert.Equal(["USER", "PID", "COMMAND"], result.Titles);
      var row = Assert.Single(result.Processes);
      Assert.Equal(3, row.Count);
      Assert.Equal("/bin/sh -c echo hi", row[2]);
    }

    [Theory]
    [InlineData("img@sha256:abcdef", true, "ignored", "img@sha256:abcdef")]
    [InlineData("repo/app:1.0", true, "ignored", "repo/app:1.0")]
    [InlineData("img", true, "ignored", "img:latest")]
    [InlineData("img", false, "edge", "img:edge")]
    public async Task PullAsync_AppendsDefaultTagOnlyForUntaggedImages(
        string image, bool useDefaultTag, string tag, string expectedRef)
    {
      RequirePosixShellFixture();
      var dir = CreateOutputDirectory("podman-pull-ref");
      var record = Path.Combine(dir, "args.txt");
      WriteExecutable(Path.Combine(dir, "podman"), $"""
          #!/bin/sh
          printf '%s\n' "$@" > '{record}'
          exit 0
          """);
      var driver = CreateImageDriverFromDirectory(dir);

      var result = useDefaultTag
          ? await driver.PullAsync(new DriverContext("podman"), image, cancellationToken: TestContext.Current.CancellationToken)
          : await driver.PullAsync(new DriverContext("podman"), image, tag, cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      Assert.Equal(["pull", expectedRef], await ReadArgsAsync(record));
    }

    [Fact]
    public async Task RunAsync_CmdHealthCheckWithSpacedArgument_ShellQuotesHealthTokens()
    {
      RequirePosixShellFixture();
      var dir = CreateOutputDirectory("podman-health-cmd");
      var record = Path.Combine(dir, "args.txt");
      WriteExecutable(Path.Combine(dir, "podman"), $"""
          #!/bin/sh
          printf '%s\n' "$@" > '{record}'
          echo 'ctr'
          """);
      var driver = CreateContainerDriverFromDirectory(dir);

      var result = await driver.RunAsync(
          new DriverContext("podman"),
          new ContainerCreateConfig
          {
            Image = "alpine",
            Detach = true,
            HealthCheck = new HealthCheckConfig { Test = ["CMD", "/bin/check", "arg with spaces"] }
          },
          TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      var args = await ReadArgsAsync(record);
      var healthIndex = Array.IndexOf(args, "--health-cmd");
      Assert.True(healthIndex >= 0);
      Assert.Equal("/bin/check 'arg with spaces'", args[healthIndex + 1]);
    }

    private static ContainerProcesses InvokeParseTopOutput(string output)
    {
      var method = typeof(PodmanCliContainerDriver).GetMethod(
          "ParseTopOutput",
          BindingFlags.NonPublic | BindingFlags.Static);
      Assert.NotNull(method);
      return (ContainerProcesses)method.Invoke(null, [output])!;
    }

    private static string Return(string body) => $"""
        #!/bin/sh
        {body}
        """;

    private static PodmanCliContainerDriver CreateContainerDriver(string script)
    {
      var dir = CreatePodmanDirectory("podman-drift-container", script);
      return CreateContainerDriverFromDirectory(dir);
    }

    private static PodmanCliContainerDriver CreateContainerDriverFromDirectory(string dir)
    {
      var driver = new PodmanCliContainerDriver(new PodmanResolver(dir));
      driver.Initialize(new DriverContext("podman"));
      return driver;
    }

    private static PodmanCliImageDriver CreateImageDriverFromDirectory(string dir)
    {
      var driver = new PodmanCliImageDriver(new PodmanResolver(dir));
      driver.Initialize(new DriverContext("podman"));
      return driver;
    }

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

    private static async Task<string[]> ReadArgsAsync(string record)
    {
      var text = await File.ReadAllTextAsync(record, TestContext.Current.CancellationToken);
      return text.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
    }

    private static async Task<string[]> ReadLinesEventuallyAsync(string record)
    {
      for (var i = 0; i < 500; i++)
      {
        if (File.Exists(record))
          return await File.ReadAllLinesAsync(record, TestContext.Current.CancellationToken);
        await Task.Delay(10, TestContext.Current.CancellationToken);
      }

      return await File.ReadAllLinesAsync(record, TestContext.Current.CancellationToken);
    }

    private static void RequirePosixShellFixture()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fixture; not applicable on Windows");
    }

    private sealed class PodmanResolver(string directory) : IPodmanBinaryResolver
    {
      private readonly PodmanBinary _binary = new(directory, "podman", SudoMechanism.None, null!, PodmanBinaryType.PodmanClient);
      public PodmanBinary[] Binaries => [_binary];
      public PodmanBinary MainPodmanClient => _binary;
      public PodmanBinary PodmanRemote => _binary;
      public PodmanBinary Resolve(string binary) => _binary;
      public string ResolveBinaryPath(string podmanCommand) => _binary.FqPath;
    }
  }
}
