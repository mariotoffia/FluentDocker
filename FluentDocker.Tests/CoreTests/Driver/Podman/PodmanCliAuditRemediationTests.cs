using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Podman.Cli;
using FluentDocker.Drivers.Podman.Cli.Binary;
using FluentDocker.Drivers.Podman.Cli.Components;
using FluentDocker.Model.Common;
using FluentDocker.Model.Drivers;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Podman
{
  [Trait("Category", "Unit")]
  [Trait("Requires", "PosixShell")]
  public class PodmanCliAuditRemediationTests
  {
    private sealed class AttachProbeDriver : PodmanCliDriverBase
    {
      public AttachResult Attach() => ExecuteAttachProcess("attach ctr");
    }

    [Fact]
    public void Attach_WithPasswordSudo_ThrowsBeforeOwningStdin()
    {
      var driver = new AttachProbeDriver();
      driver.Initialize(new DriverContext("podman")
      {
        Sudo = SudoMechanism.Password,
        SudoPassword = "secret"
      });

      var ex = Assert.Throws<NotSupportedException>(() => driver.Attach());
      Assert.Contains("attach stdin belongs to the caller", ex.Message);
    }

    [Fact]
    public async Task StreamLogsAsync_InterleavesStdoutAndStderrLines()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fixture; not applicable on Windows");

      var script = """
          #!/bin/sh
          if [ "$1" = "logs" ]; then
            printf 'stdout-line\n'
            printf 'stderr-line\n' >&2
            exit 0
          fi
          echo "unexpected $*" >&2
          exit 7
          """;
      var driver = CreateStreamDriver(script);
      var lines = new List<string>();

      await foreach (var line in driver.StreamLogsAsync(
          new DriverContext("podman"),
          "ctr",
          new StreamLogsConfig { Follow = false },
          TestContext.Current.CancellationToken))
        lines.Add(line);

      Assert.Contains("stdout-line", lines);
      Assert.Contains("stderr-line", lines);
    }

    [Fact]
    public async Task AutoStartMachine_StartingMachine_WaitsWithoutCallingStart()
    {
      if (!PodmanCliDriverPack.MachineManagementApplies())
        Assert.Skip("Podman machine management only applies on macOS/Windows");

      var dir = CreateOutputDirectory("podman-machine-starting");
      var record = Path.Combine(dir, "calls.txt");
      var script = $$"""
          #!/bin/sh
          echo "$*" >> "{{record}}"
          if [ "$1" = "machine" ] && [ "$2" = "list" ]; then
            printf '[{"Name":"podman-machine-default","Default":true,"Running":false,"Starting":true}]\n'
            exit 0
          fi
          if [ "$1" = "machine" ] && [ "$2" = "start" ]; then
            echo "start must not be called" >&2
            exit 9
          fi
          if [ "$1" = "info" ]; then
            exit 0
          fi
          echo "unexpected $*" >&2
          exit 8
          """;
      WriteExecutable(Path.Combine(dir, "podman"), script);

      var pack = new PodmanCliDriverPack();
      await pack.InitializeAsync(new DriverContext("podman")
      {
        SearchPaths = [dir],
        AutoStartMachine = new AutoStartMachineConfig()
      }, TestContext.Current.CancellationToken);

      Assert.DoesNotContain("machine start", File.ReadAllText(record));
    }

    [Fact]
    public async Task StreamStatsAsync_RealPodmanPrettyJsonArray_YieldsEachItem()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fixture; not applicable on Windows");

      var script = """
          #!/bin/sh
          if [ "$1" = "stats" ]; then
            cat <<'JSON'
          [
            {
              "id": "abc123",
              "name": "web",
              "cpu_percent": "1.50%",
              "mem_usage": "10MiB / 1GiB",
              "mem_percent": "0.98%",
              "net_io": "1kB / 2kB",
              "block_io": "3kB / 4kB",
              "pids": "2"
            },
            {
              "id": "def456",
              "name": "worker",
              "cpu_percent": "2.50%",
              "mem_usage": "20MiB / 1GiB",
              "mem_percent": "1.95%",
              "net_io": "5kB / 6kB",
              "block_io": "7kB / 8kB",
              "pids": "3"
            }
          ]
          JSON
            exit 0
          fi
          echo "unexpected $*" >&2
          exit 7
          """;
      var driver = CreateStreamDriver(script);
      var stats = new List<ContainerStats>();

      await foreach (var item in driver.StreamStatsAsync(
          new DriverContext("podman"),
          config: new StreamStatsConfig { Stream = false },
          cancellationToken: TestContext.Current.CancellationToken))
        stats.Add(item);

      Assert.Equal(2, stats.Count);
      Assert.Equal("abc123", stats[0].ContainerId);
      Assert.Equal("worker", stats[1].Name);
      Assert.Equal(2.50, stats[1].CpuPercentage, 2);
    }

    private static PodmanCliStreamDriver CreateStreamDriver(string script)
    {
      var dir = CreateOutputDirectory("podman-stream");
      WriteExecutable(Path.Combine(dir, "podman"), script);

      var resolver = new Mock<IPodmanBinaryResolver>();
      resolver.Setup(r => r.Resolve("podman"))
          .Returns(new PodmanBinary(dir, "podman", SudoMechanism.None, null!, PodmanBinaryType.PodmanClient));

      var driver = new PodmanCliStreamDriver(resolver.Object);
      driver.Initialize(new DriverContext("podman"));
      return driver;
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
  }
}
