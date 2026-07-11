using System;
using System.IO;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers.Podman.Cli;
using FluentDocker.Drivers.Podman.Cli.Binary;
using FluentDocker.Drivers.Podman.Cli.Components;
using FluentDocker.Model.Common;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Podman
{
  /// <summary>
  /// Task 11 fixes: P-M2 (auto-start must not guess an arbitrary machine when none is flagged
  /// default) and P-M3 (buffered <c>stats</c> must carry <c>--no-reset</c>, symmetric with the
  /// streaming path).
  /// </summary>
  [Trait("Category", "Unit")]
  [Trait("Requires", "PosixShell")]
  public class PodmanCliTask11RemediationTests
  {
    #region P-M2 — auto-start ambiguous-machine guard

    [Fact]
    public async Task AutoStartMachine_MultipleMachinesNoneDefault_ThrowsNamingMachineNameConfig()
    {
      RequirePosixShellFixture();
      SkipUnlessMachineManagementApplies();

      var dir = CreateOutputDirectory("podman-autostart-ambiguous");
      var record = Path.Combine(dir, "calls.txt");
      // machine start / info both succeed trivially: pre-fix this lets AutoStartMachineCoreAsync
      // silently pick the first machine in the list and complete successfully — exactly the bug
      // (arbitrary pick instead of an actionable error). Post-fix, the new guard throws right
      // after `machine list`, so start/info must never be recorded below.
      WriteExecutable(Path.Combine(dir, "podman"), $$"""
          #!/bin/sh
          echo "$*" >> "{{record}}"
          case "$*" in
            *"machine list --format json"*)
              echo '[{"Name":"m1","Default":false,"Running":false},{"Name":"m2","Default":false,"Running":false}]'
              exit 0
              ;;
            *"machine start"*)
              exit 0
              ;;
            *"info"*)
              exit 0
              ;;
          esac
          echo "unexpected $*" >&2
          exit 9
          """);

      var pack = new PodmanCliDriverPack();
      var ex = await Assert.ThrowsAsync<PodmanMachineNotRunningException>(() =>
          pack.InitializeAsync(new DriverContext("podman")
          {
            SearchPaths = [dir],
            AutoStartMachine = new AutoStartMachineConfig()
          }, TestContext.Current.CancellationToken));

      Assert.Contains("AutoStartMachineConfig.MachineName", ex.Message);
      Assert.DoesNotContain("machine start", File.ReadAllText(record));
    }

    [Fact]
    public async Task AutoStartMachine_SingleMachineNotDefault_IsUsedWithoutThrowing()
    {
      RequirePosixShellFixture();
      SkipUnlessMachineManagementApplies();

      var dir = CreateOutputDirectory("podman-autostart-single-nondefault");
      WriteExecutable(Path.Combine(dir, "podman"), """
          #!/bin/sh
          case "$*" in
            *"machine list --format json"*)
              echo '[{"Name":"only","Default":false,"Running":true}]'
              exit 0
              ;;
          esac
          echo "unexpected $*" >&2
          exit 9
          """);

      var pack = new PodmanCliDriverPack();
      await pack.InitializeAsync(new DriverContext("podman")
      {
        SearchPaths = [dir],
        AutoStartMachine = new AutoStartMachineConfig()
      }, TestContext.Current.CancellationToken);

      // No assertion failure above means the single non-default machine was accepted (already
      // Running, so the fake CLI's catch-all `exit 9` for any other command was never hit).
    }

    [Fact]
    public async Task AutoStartMachine_ExplicitMachineName_SelectsNamedMachineRegardlessOfDefault()
    {
      RequirePosixShellFixture();
      SkipUnlessMachineManagementApplies();

      var dir = CreateOutputDirectory("podman-autostart-named");
      WriteExecutable(Path.Combine(dir, "podman"), """
          #!/bin/sh
          case "$*" in
            *"machine list --format json"*)
              echo '[{"Name":"other","Default":false,"Running":false},{"Name":"target-machine","Default":false,"Running":true}]'
              exit 0
              ;;
          esac
          echo "unexpected $*" >&2
          exit 9
          """);

      var pack = new PodmanCliDriverPack();
      await pack.InitializeAsync(new DriverContext("podman")
      {
        SearchPaths = [dir],
        AutoStartMachine = new AutoStartMachineConfig { MachineName = "target-machine" }
      }, TestContext.Current.CancellationToken);

      // Picking "other" (not running) would have driven the fake CLI into `machine start other`,
      // which is not handled above and would exit 9 — reaching here proves the named machine,
      // already Running, was the one selected.
    }

    #endregion

    #region P-M3 — buffered stats --no-reset

    [Fact]
    public async Task StatsAsync_BufferedCommand_IncludesNoReset()
    {
      RequirePosixShellFixture();

      var dir = CreateOutputDirectory("podman-stats-no-reset");
      var record = Path.Combine(dir, "stats-argv.txt");
      WriteExecutable(Path.Combine(dir, "podman"), $$"""
          #!/bin/sh
          if [ "$1" = "stats" ]; then
            echo "$*" >> "{{record}}"
            echo '{"id":"abc123","name":"n","cpu_percent":"0.00%","mem_usage":"0B / 0B","mem_percent":"0.00%","net_io":"0B / 0B","block_io":"0B / 0B","pids":"0"}'
            exit 0
          fi
          echo "unexpected $*" >&2
          exit 9
          """);

      var driver = new PodmanCliContainerDriver(new PodmanResolver(dir));
      driver.Initialize(new DriverContext("podman"));

      var result = await driver.StatsAsync(
          new DriverContext("podman"), "ctr123", TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      var argv = await File.ReadAllTextAsync(record, TestContext.Current.CancellationToken);
      Assert.Contains("--no-reset", argv);
    }

    #endregion

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

    private static void SkipUnlessMachineManagementApplies()
    {
      if (!PodmanCliDriverPack.MachineManagementApplies())
        Assert.Skip("Podman machine auto-start applies only on macOS/Windows");
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
