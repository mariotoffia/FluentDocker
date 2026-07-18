using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Drivers.Podman.Cli
{
  // Auto-start-machine logic split out of PodmanCliDriverPack.cs to keep that file under the
  // 500-line cap (the P-M2 ambiguous-machine guard pushed it over). Purely a physical split —
  // same partial class; MachineInfo/MachineInitConfig resolve via the enclosing
  // FluentDocker.Drivers.Podman namespace, same as before the split.
  public partial class PodmanCliDriverPack
  {
    #region Auto-Start Machine

    /// <summary>
    /// Process-wide, per-machine-name async locks so concurrent kernel builds cannot race to
    /// start the same Podman machine (start/init are not safe to run twice in parallel).
    /// Keyed by the configured machine name (or a sentinel for the default machine).
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> MachineLocks = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whether this library's Podman machine auto-start applies on the current platform
    /// (macOS/Windows, where Podman always needs a VM). <c>podman machine</c> itself also
    /// exists on Linux, but native Linux typically runs Podman without a VM, so this library
    /// never auto-starts a machine there — a Linux machine must be started externally.
    /// Public static so the platform gate can be unit-tested through the public
    /// surface (the pack's auto-start path itself drives the real <c>podman machine</c> CLI).
    /// </summary>
    public static bool MachineManagementApplies() => FdOs.IsOsx() || FdOs.IsWindows();

    /// <summary>
    /// Computes the per-machine serialization key used to ensure concurrent kernel builds do
    /// not race to start/init the same Podman machine. An unset name normalizes to a shared
    /// <c>"default"</c> sentinel, so a null-name build and an explicit <c>MachineName = "default"</c>
    /// build serialize on the same gate instead of two different ones (conservative
    /// over-serialization is harmless here). Public static so the keying is unit-testable
    /// without internals access.
    /// </summary>
    public static string MachineLockKey(string machineName)
        => string.IsNullOrEmpty(machineName) ? "default" : machineName;

    private async Task AutoStartMachineAsync(
        DriverContext context, CancellationToken cancellationToken)
    {
      if (!MachineManagementApplies())
      {
        throw new DriverException(
            "Auto-start of a Podman machine is only supported on macOS/Windows by this library. " +
            "podman machine itself exists on Linux, but a machine on native Linux must be started " +
            "externally (e.g. `podman machine start`); remove WithAutoStartMachine on Linux.",
            ErrorCodes.Driver.CapabilityNotSupported);
      }

      var key = MachineLockKey(context.AutoStartMachine.MachineName);
      var gate = MachineLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));

      await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
      try
      {
        await AutoStartMachineCoreAsync(context, cancellationToken).ConfigureAwait(false);
      }
      finally
      {
        gate.Release();
      }
    }

    private async Task AutoStartMachineCoreAsync(
        DriverContext context, CancellationToken cancellationToken)
    {
      var config = context.AutoStartMachine;
      var listResult = await _machineDriver.ListAsync(context, cancellationToken).ConfigureAwait(false);

      if (!listResult.Success)
      {
        await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        listResult = await _machineDriver.ListAsync(context, cancellationToken).ConfigureAwait(false);
      }

      if (!listResult.Success)
        throw new PodmanMachineNotRunningException(
            $"Failed to list Podman machines: {listResult.Error}");

      MachineInfo target;
      if (!string.IsNullOrEmpty(config.MachineName))
        target = listResult.Data.FirstOrDefault(
            m => string.Equals(m.Name, config.MachineName,
                StringComparison.OrdinalIgnoreCase));
      else
      {
        target = listResult.Data.FirstOrDefault(m => m.Default);
        if (target == null)
        {
          // No machine is flagged default (e.g. after `podman system connection` edits) and more
          // than one exists: guessing which one to start can target the wrong machine and hang
          // for the full readiness timeout against an unrelated VM. Fail fast with guidance
          // instead (P-M2).
          if (listResult.Data.Count > 1)
            throw new PodmanMachineNotRunningException(
                "Multiple Podman machines exist and none is flagged default; set " +
                "AutoStartMachineConfig.MachineName to choose which machine to start.",
                isTransient: false);
          target = listResult.Data.FirstOrDefault(); // 0 or 1 machine: safe (null or the single one)
        }
      }

      if (target != null && target.Running)
        return; // Machine is already running

      if (target != null && target.Starting)
      {
        await WaitForMachineReadyAsync(context, cancellationToken).ConfigureAwait(false);
        return;
      }

      if (target != null)
      {
        var startResult = await _machineDriver.StartAsync(
            context, target.Name, cancellationToken).ConfigureAwait(false);

        if (!startResult.Success)
        {
          try
          {
            await WaitForMachineReadyAsync(context, cancellationToken).ConfigureAwait(false);
            return;
          }
          catch (PodmanMachineNotRunningException)
          {
            throw new PodmanMachineNotRunningException(
                $"Failed to start Podman machine '{target.Name}': {startResult.Error}");
          }
        }

        await WaitForMachineReadyAsync(context, cancellationToken).ConfigureAwait(false);
        return;
      }

      if (!config.CreateIfNotExists)
        throw new PodmanMachineNotRunningException(
            $"No Podman machine found" +
            (string.IsNullOrEmpty(config.MachineName)
                ? ". "
                : $" named '{config.MachineName}'. ") +
            "Start one with: podman machine init && podman machine start");

      var displayName = string.IsNullOrEmpty(config.MachineName)
          ? "default machine" : $"machine '{config.MachineName}'";
      var initConfig = BuildAutoStartInitConfig(config);

      var initResult = await _machineDriver.InitAsync(
          context, initConfig, cancellationToken).ConfigureAwait(false);

      if (!initResult.Success)
        throw new PodmanMachineNotRunningException(
            $"Failed to initialize Podman {displayName}: {initResult.Error}");

      await WaitForMachineReadyAsync(context, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Maps an <see cref="AutoStartMachineConfig"/> to the <see cref="MachineInitConfig"/> used to
    /// auto-create a machine. When <see cref="AutoStartMachineConfig.MachineName"/> is unspecified
    /// the name is left NULL so <c>podman machine init</c> targets its real built-in default rather
    /// than a literal machine called "default". Public static so the name-omission is unit-testable
    /// through the public surface without internals access.
    /// </summary>
    public static MachineInitConfig BuildAutoStartInitConfig(AutoStartMachineConfig config)
    {
      ArgumentNullException.ThrowIfNull(config);

      return new MachineInitConfig
      {
        Name = string.IsNullOrEmpty(config.MachineName) ? null : config.MachineName,
        Cpus = config.InitCpus,
        MemoryMiB = config.InitMemoryMiB,
        DiskSizeGiB = config.InitDiskSizeGiB,
        Rootful = config.InitRootful,
        Now = true // Start immediately after init
      };
    }

    // ponytail: a fixed 1s poll / 60s ceiling is enough for a local podman machine to answer
    // `info` after start/init. Upgrade path: surface these as knobs on AutoStartMachineConfig if a
    // slower host or CI ever needs a longer readiness budget.
    private static readonly TimeSpan MachineReadyPollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MachineReadyTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Polls <c>podman info</c> after a start/init until the machine answers or
    /// <see cref="MachineReadyTimeout"/> elapses. A freshly started machine is not immediately
    /// usable — the VM/connection needs a moment — so returning before it is ready would hand the
    /// caller a machine that fails the next command. Honors caller cancellation.
    /// </summary>
    private async Task WaitForMachineReadyAsync(
        DriverContext context, CancellationToken cancellationToken)
    {
      using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
      budget.CancelAfter(MachineReadyTimeout);

      try
      {
        while (true)
        {
          var ping = await _systemDriver.PingAsync(context, budget.Token).ConfigureAwait(false);
          if (ping.Success)
            return;

          await Task.Delay(MachineReadyPollInterval, budget.Token).ConfigureAwait(false);
        }
      }
      catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
      {
        throw new PodmanMachineNotRunningException(
            $"Podman machine did not become ready within {MachineReadyTimeout.TotalSeconds:0}s after start/init.");
      }
    }

    #endregion
  }
}
