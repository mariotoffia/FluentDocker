using System;
using System.IO;
using System.Threading.Tasks;
using FluentDocker.Drivers.Podman;
using FluentDocker.Drivers.Podman.Cli;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Podman
{
  [Trait("Category", "Unit")]
  [Trait("Requires", "PosixShell")]
  public class PodmanCliMachineEmptyListTests
  {
    [Fact]
    public async Task AutoStartMachine_CreateIfNotExists_DoesNotRetrySuccessfulEmptyMachineList()
    {
      RequirePosixShellFixture();
      if (!PodmanCliDriverPack.MachineManagementApplies())
        Assert.Skip("Podman machine auto-start applies only on macOS/Windows");

      var dir = CreateOutputDirectory("podman-machine-empty-list");
      var listCount = Path.Combine(dir, "list-count.txt");
      WriteExecutable(Path.Combine(dir, "podman"), $"""
          #!/bin/sh
          case "$*" in
            *"machine list --format json"*)
              printf 'x' >> '{listCount}'
              echo '[]'
              exit 0
              ;;
            *"machine init --now"*)
              exit 0
              ;;
            *"info"*)
              echo 'ok'
              exit 0
              ;;
          esac
          exit 2
          """);
      var pack = new PodmanCliDriverPack();

      await pack.InitializeAsync(new DriverContext("podman")
      {
        SearchPaths = [dir],
        AutoStartMachine = new AutoStartMachineConfig { CreateIfNotExists = true }
      }, TestContext.Current.CancellationToken);

      Assert.Equal(1, (await File.ReadAllTextAsync(listCount, TestContext.Current.CancellationToken)).Length);
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
  }
}
