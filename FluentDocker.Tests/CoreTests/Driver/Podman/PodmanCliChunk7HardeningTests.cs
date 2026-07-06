using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Podman;
using FluentDocker.Drivers.Podman.Cli.Binary;
using FluentDocker.Drivers.Podman.Cli.Components;
using FluentDocker.Model.Common;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Podman
{
  [Trait("Category", "Unit")]
  [Trait("Requires", "PosixShell")]
  public class PodmanCliChunk7HardeningTests
  {
    [Fact]
    public async Task LoginAsync_WritesPasswordStdinAsUtf8()
    {
      RequirePosixShellFixture();
      var dir = CreateOutputDirectory("podman-login-utf8");
      var capture = Path.Combine(dir, "stdin.bin");
      WriteExecutable(Path.Combine(dir, "podman"), $"""
          #!/bin/sh
          cat > '{capture}'
          exit 0
          """);
      var driver = new PodmanCliAuthDriver(new PodmanResolver(dir));
      driver.Initialize(new DriverContext("podman"));

      var password = "päss-🔒";
      var result = await driver.LoginAsync(
          new DriverContext("podman"),
          new RegistryLoginConfig { Server = "registry.example", Username = "u", Password = password },
          TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      Assert.Equal(Encoding.UTF8.GetBytes(password), await File.ReadAllBytesAsync(capture, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task MachineRemoveAsync_WhenChildClosesStdin_PreservesStderrAndExitCode()
    {
      RequirePosixShellFixture();
      var dir = CreateOutputDirectory("podman-stdin-failure");
      WriteExecutable(Path.Combine(dir, "podman"), """
          #!/bin/sh
          echo 'podman real error' >&2
          exit 23
          """);
      var driver = new PodmanCliMachineDriver(new PodmanResolver(dir));
      driver.Initialize(new DriverContext("podman"));

      var result = await driver.RemoveAsync(
          new DriverContext("podman"),
          "vm",
          force: false,
          TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(23, result.ExitCode);
      Assert.Contains("podman real error", result.Error);
    }

    [Fact]
    public async Task StreamLogEntriesAsync_LabelsStdoutAndStderrAndHonorsFilters()
    {
      RequirePosixShellFixture();
      var dir = CreateOutputDirectory("podman-log-sources");
      WriteExecutable(Path.Combine(dir, "podman"), """
          #!/bin/sh
          echo stdout-line
          echo stderr-line >&2
          exit 0
          """);
      IStreamDriver driver = new PodmanCliStreamDriver(new PodmanResolver(dir));
      ((PodmanCliStreamDriver)driver).Initialize(new DriverContext("podman"));

      var both = await ReadLogEntriesAsync(driver, new StreamLogsConfig { Follow = false });
      Assert.Contains(both, e => e.Source == LogStreamSource.Stdout && e.Line == "stdout-line");
      Assert.Contains(both, e => e.Source == LogStreamSource.Stderr && e.Line == "stderr-line");

      var stderrOnly = await ReadLogEntriesAsync(driver, new StreamLogsConfig { Follow = false, Stdout = false, Stderr = true });
      Assert.DoesNotContain(stderrOnly, e => e.Source == LogStreamSource.Stdout);
      Assert.Contains(stderrOnly, e => e.Source == LogStreamSource.Stderr && e.Line == "stderr-line");
    }

    [Fact]
    public async Task StreamLogsAsync_PrefixesStderrLines()
    {
      RequirePosixShellFixture();
      var dir = CreateOutputDirectory("podman-log-prefix");
      WriteExecutable(Path.Combine(dir, "podman"), """
          #!/bin/sh
          echo stdout-line
          echo stderr-line >&2
          exit 0
          """);
      var driver = new PodmanCliStreamDriver(new PodmanResolver(dir));
      driver.Initialize(new DriverContext("podman"));

      var lines = new List<string>();
      await foreach (var line in driver.StreamLogsAsync(
          new DriverContext("podman"),
          "ctr",
          new StreamLogsConfig { Follow = false },
          TestContext.Current.CancellationToken))
      {
        lines.Add(line);
      }

      Assert.Contains("stdout-line", lines);
      Assert.Contains("[stderr] stderr-line", lines);
    }

    [Fact]
    public async Task PlayAsync_PodlessManifestOutput_ReturnsSuccessfulEmptyResult()
    {
      RequirePosixShellFixture();
      var driver = CreateKubernetesDriver("""
          #!/bin/sh
          echo 'ConfigMap/app-config created'
          echo 'PersistentVolumeClaim/app-data created'
          exit 0
          """);

      var result = await driver.PlayAsync(
          new DriverContext("podman"),
          new KubePlayConfig { YamlPath = "podless.yaml" },
          TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      Assert.Empty(result.Data.Pods);
    }

    [Fact]
    public async Task PlayAsync_VolumeNameLine_IsNotMisparsedAsPodId()
    {
      RequirePosixShellFixture();
      var driver = CreateKubernetesDriver("""
          #!/bin/sh
          echo 'abcdefghijkl'
          exit 0
          """);

      var result = await driver.PlayAsync(
          new DriverContext("podman"),
          new KubePlayConfig { YamlPath = "volume-only.yaml" },
          TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      Assert.Empty(result.Data.Pods);
    }

    [Fact]
    public async Task RemoveAsync_ParsesDeletedAndUntaggedImageLines()
    {
      RequirePosixShellFixture();
      var driver = CreateImageDriver("""
          #!/bin/sh
          echo 'Untagged: localhost/app:latest'
          echo 'Deleted: sha256:abc123'
          exit 0
          """);

      var result = await driver.RemoveAsync(
          new DriverContext("podman"),
          "localhost/app:latest",
          cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      Assert.Equal(["sha256:abc123"], result.Data.Deleted);
      Assert.Equal(["localhost/app:latest"], result.Data.Untagged);
    }

    [Theory]
    [InlineData("Error: unable to connect to Podman socket: dial unix /run/podman/podman.sock: connect: no such file or directory")]
    [InlineData("dial unix /run/podman/podman.sock: connect: connection refused")]
    public async Task CommandFailures_ClassifyPodmanSocketOutagesAsConnectionFailed(string error)
    {
      RequirePosixShellFixture();
      var driver = CreateImageDriver($"""
          #!/bin/sh
          echo '{error}' >&2
          exit 125
          """);

      var result = await driver.RemoveAsync(
          new DriverContext("podman"),
          "alpine",
          cancellationToken: TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Api.ConnectionFailed, result.ErrorCode);
      Assert.Equal(125, result.ExitCode);
    }

    [Fact]
    public async Task StreamLogsAsync_WithDetails_DoesNotInjectUnsupportedFlag()
    {
      RequirePosixShellFixture();
      var dir = CreateOutputDirectory("podman-log-details");
      var capture = Path.Combine(dir, "args.txt");
      WriteExecutable(Path.Combine(dir, "podman"), $"""
          #!/bin/sh
          echo "$*" > '{capture}'
          echo logline
          exit 0
          """);
      var driver = new PodmanCliStreamDriver(new PodmanResolver(dir));
      driver.Initialize(new DriverContext("podman"));

      var lines = new List<string>();
      await foreach (var line in driver.StreamLogsAsync(
          new DriverContext("podman"),
          "ctr",
          new StreamLogsConfig { Follow = false, Details = true },
          TestContext.Current.CancellationToken))
      {
        lines.Add(line);
      }

      Assert.Contains("logline", lines);
      var args = await File.ReadAllTextAsync(capture, TestContext.Current.CancellationToken);
      Assert.Contains("logs", args);
      Assert.DoesNotContain("--details", args);
    }

    [Fact]
    public async Task PlayAsync_WhenSocketOutage_ClassifiesConnectionFailed()
    {
      RequirePosixShellFixture();
      var driver = CreateKubernetesDriver("""
          #!/bin/sh
          echo 'Error: unable to connect to Podman socket: dial unix /run/podman/podman.sock: connect: no such file or directory' >&2
          exit 125
          """);

      var result = await driver.PlayAsync(
          new DriverContext("podman"),
          new KubePlayConfig { YamlPath = "app.yaml" },
          TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Api.ConnectionFailed, result.ErrorCode);
    }

    [Fact]
    public async Task GenerateAsync_WhenSocketOutage_ClassifiesConnectionFailed()
    {
      RequirePosixShellFixture();
      var driver = CreateKubernetesDriver("""
          #!/bin/sh
          echo 'Error: unable to connect to Podman socket: dial unix /run/podman/podman.sock: connect: connection refused' >&2
          exit 125
          """);

      var result = await driver.GenerateAsync(
          new DriverContext("podman"),
          "mypod",
          TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Api.ConnectionFailed, result.ErrorCode);
    }

    private static async Task<List<LogEntry>> ReadLogEntriesAsync(IStreamDriver driver, StreamLogsConfig config)
    {
      var entries = new List<LogEntry>();
      await foreach (var entry in driver.StreamLogEntriesAsync(
          new DriverContext("podman"),
          "ctr",
          config,
          TestContext.Current.CancellationToken))
      {
        entries.Add(entry);
      }

      return entries;
    }

    private static PodmanCliKubernetesDriver CreateKubernetesDriver(string script)
    {
      var dir = CreatePodmanDirectory("podman-kube-ch7", script);
      var driver = new PodmanCliKubernetesDriver(new PodmanResolver(dir));
      driver.Initialize(new DriverContext("podman"));
      return driver;
    }

    private static PodmanCliImageDriver CreateImageDriver(string script)
    {
      var dir = CreatePodmanDirectory("podman-image-ch7", script);
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
