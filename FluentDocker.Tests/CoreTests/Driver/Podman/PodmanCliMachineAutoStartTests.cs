using System;
using System.Reflection;
using FluentDocker.Drivers.Podman;
using FluentDocker.Drivers.Podman.Cli;
using FluentDocker.Drivers.Podman.Cli.Components;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Podman
{
  /// <summary>
  /// Tests the public seams that make Podman machine auto-start platform- and race-safe
  /// (FIX-4): the platform gate (<see cref="PodmanCliDriverPack.MachineManagementApplies"/>)
  /// and the per-machine serialization key (<see cref="PodmanCliDriverPack.MachineLockKey"/>).
  /// The auto-start path itself drives the real <c>podman machine</c> CLI and cannot be unit
  /// tested, so the gate/keying logic is verified through these public static seams.
  /// </summary>
  [Trait("Category", "Unit")]
  public class PodmanCliMachineAutoStartTests
  {
    [Fact]
    public void MachineManagementApplies_MatchesCurrentPlatform()
    {
      // Podman machine only exists on macOS/Windows; native Linux runs without a VM.
      var expected = OperatingSystem.IsMacOS() || OperatingSystem.IsWindows();
      Assert.Equal(expected, PodmanCliDriverPack.MachineManagementApplies());
    }

    [Fact]
    public void MachineLockKey_NullOrEmpty_NormalizesToDefaultName()
    {
      // An unset name normalizes to a shared "default" lock-key sentinel so the per-machine gate
      // is stable and consistent. (The start/init path itself now OMITS the name so podman targets
      // its real built-in default machine — the lock key just needs to be stable and shared.)
      Assert.Equal("default", PodmanCliDriverPack.MachineLockKey(null!));
      Assert.Equal("default", PodmanCliDriverPack.MachineLockKey(""));
    }

    [Fact]
    public void MachineLockKey_NullAndExplicitDefault_ShareOneGate()
    {
      // Regression: a null-name build (resolves to "default") and an explicit
      // MachineName = "default" build target the same machine, so they must serialize on the
      // same key — otherwise the per-machine lock fails to prevent a concurrent start/init race.
      Assert.Equal(
          PodmanCliDriverPack.MachineLockKey(null!),
          PodmanCliDriverPack.MachineLockKey("default"));
    }

    [Fact]
    public void MachineLockKey_NamedMachine_UsesName()
    {
      Assert.Equal("dev", PodmanCliDriverPack.MachineLockKey("dev"));
    }

    [Fact]
    public void MachineLockKey_DistinctNames_ProduceDistinctKeys()
    {
      // Distinct keys => distinct SemaphoreSlim buckets => concurrent starts of different
      // machines are NOT serialized against each other (only same-name builds are).
      Assert.NotEqual(
          PodmanCliDriverPack.MachineLockKey("a"),
          PodmanCliDriverPack.MachineLockKey("b"));
    }

    #region BuildAutoStartInitConfig (P3 — name omission)

    [Fact]
    public void BuildAutoStartInitConfig_NullMachineName_LeavesNameNull()
    {
      // Documented as "default machine" when unspecified: the init config must leave Name NULL so
      // podman targets its real built-in default instead of a literal machine called "default".
      var config = new AutoStartMachineConfig { MachineName = null! };

      var init = PodmanCliDriverPack.BuildAutoStartInitConfig(config);

      Assert.Null(init.Name);
      Assert.True(init.Now);
    }

    [Fact]
    public void BuildAutoStartInitConfig_EmptyMachineName_LeavesNameNull()
    {
      var config = new AutoStartMachineConfig { MachineName = "" };

      var init = PodmanCliDriverPack.BuildAutoStartInitConfig(config);

      Assert.Null(init.Name);
    }

    [Fact]
    public void BuildAutoStartInitConfig_NamedMachine_PassesNameAndResourcesThrough()
    {
      var config = new AutoStartMachineConfig
      {
        MachineName = "dev",
        InitCpus = 4,
        InitMemoryMiB = 4096,
        InitDiskSizeGiB = 50,
        InitRootful = true
      };

      var init = PodmanCliDriverPack.BuildAutoStartInitConfig(config);

      Assert.Equal("dev", init.Name);
      Assert.Equal(4, init.Cpus);
      Assert.Equal(4096, init.MemoryMiB);
      Assert.Equal(50, init.DiskSizeGiB);
      Assert.True(init.Rootful);
      Assert.True(init.Now);
    }

    [Fact]
    public void BuildAutoStartInitConfig_NullMachineName_ProducesInitArgvWithoutName()
    {
      // End-to-end at the pure-function level: an unspecified machine name must NOT append a name
      // token to `podman machine init`, so podman uses its real default machine.
      var init = PodmanCliDriverPack.BuildAutoStartInitConfig(new AutoStartMachineConfig());

      Assert.Equal("machine init --now", InvokeBuildInitArgs(init));
    }

    private static string InvokeBuildInitArgs(MachineInitConfig config)
    {
      var method = typeof(PodmanCliMachineDriver).GetMethod(
          "BuildInitArgs",
          BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public);
      Assert.NotNull(method);
      return (string)method.Invoke(null, [config])!;
    }

    #endregion
  }
}
