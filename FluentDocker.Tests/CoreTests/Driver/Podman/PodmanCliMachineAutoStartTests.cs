using System;
using FluentDocker.Drivers.Podman.Cli;
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
      // An unset name must normalize to the SAME "default" the start/init path uses, so the key
      // is consistent with the machine actually acted upon (no "<sentinel>" that nothing else
      // shares).
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
  }
}
