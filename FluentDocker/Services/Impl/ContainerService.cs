using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Containers;
using FluentDocker.Model.Drivers;
using Microsoft.Extensions.Logging;

namespace FluentDocker.Services.Impl
{
  /// <inheritdoc />
  /// <remarks>
  /// After disposal, lifecycle state/events are deliberately suppressed instead of throwing.
  /// Lifecycle transitions are individually atomic; a single service instance is not designed
  /// for concurrent lifecycle calls (Start/Stop/Remove/Dispose) from multiple threads.
  /// </remarks>
  public partial class ContainerService : IContainerService, IServiceCapabilities
  {
    private readonly FluentDockerKernel _kernel;
    private readonly ILogger<ContainerService> _logger;
    private readonly string _driverId;
    private readonly string _containerId;
    private readonly string _image;
    private readonly string _name;
    private readonly bool _stopOnDispose;
    private readonly bool _deleteOnDispose;
    private readonly bool _deleteVolumeOnDispose;
    private readonly bool _deleteNamedVolumeOnDispose;
    private readonly Func<Dictionary<string, HostIpEndpoint[]>, string, Uri, IPEndPoint> _customResolver;
    private readonly List<LifecycleHook> _lifecycleHooks;
    private readonly ConcurrentDictionary<string, (ServiceRunningState State, Func<IServiceAsync, Task> Hook)> _hooks = [];
    private readonly object _stateLock = new();
    private volatile ServiceRunningState _state = ServiceRunningState.Unknown;

    // Short-lived inspect cache to avoid redundant API/CLI calls during wait polling.
    // Thread-safety: Single immutable record reference ensures atomic read/write
    // of both data and timestamp together, preventing torn reads.
    // The _cacheVersion counter prevents stale writes: if a state change occurs
    // while an InspectAsync is in-flight, the result is discarded rather than cached.
    private volatile InspectCacheEntry _inspectCacheEntry;
    private volatile int _cacheVersion;
    private int _disposeRemoveVersion;
    private int _inspectSequence;
    private int _lastAppliedInspectSequence;

    /// <summary>
    /// Immutable cache entry pairing inspect data with its timestamp.
    /// Using a single reference ensures atomic reads/writes.
    /// </summary>
    private sealed record InspectCacheEntry(Container Data, long Timestamp);

    /// <summary>
    /// Time-to-live in milliseconds for the InspectAsync result cache.
    /// </summary>
    public const long InspectCacheTtlMs = 500;

    /// <summary>
    /// Default upper bound, in milliseconds, for the stop/remove cleanup performed during
    /// disposal. Disposal is best-effort and must not hang indefinitely on an unresponsive
    /// daemon, so the cleanup is abandoned once this elapses. Adjustable per instance via the
    /// constructor's <c>disposeCleanupTimeout</c> parameter.
    /// </summary>
    public const int DefaultDisposeCleanupTimeoutMs = 30_000;

    private readonly TimeSpan _disposeCleanupTimeout;

    /// <summary>
    /// Creates a new container service.
    /// </summary>
    /// <param name="kernel">Kernel used to resolve container and volume driver ports.</param>
    /// <param name="driverId">Driver id registered in the kernel.</param>
    /// <param name="containerId">Container id used for driver operations.</param>
    /// <param name="image">Image reference used to create or discover the container.</param>
    /// <param name="name">Container display/name reference.</param>
    /// <param name="stopOnDispose">When true, dispose tries to stop the owned container before removal.</param>
    /// <param name="deleteOnDispose">When true, dispose removes the owned container.</param>
    /// <param name="deleteVolumeOnDispose">When true, remove also deletes anonymous volumes.</param>
    /// <param name="deleteNamedVolumeOnDispose">When true, dispose removes named volume mounts after container removal.</param>
    /// <param name="customResolver">Optional host endpoint resolver for published ports.</param>
    /// <param name="lifecycleHooks">Lifecycle hooks owned by this service instance.</param>
    /// <param name="disposeCleanupTimeout">Maximum best-effort stop/remove cleanup time during dispose.</param>
    /// <param name="initialState">Initial client-side lifecycle state.</param>
    public ContainerService(
        FluentDockerKernel kernel,
        string driverId,
        string containerId,
        string image,
        string name,
        bool stopOnDispose = true,
        bool deleteOnDispose = true,
        bool deleteVolumeOnDispose = false,
        bool deleteNamedVolumeOnDispose = false,
        Func<Dictionary<string, HostIpEndpoint[]>, string, Uri, IPEndPoint> customResolver = null,
        List<LifecycleHook> lifecycleHooks = null,
        TimeSpan? disposeCleanupTimeout = null,
        ServiceRunningState initialState = ServiceRunningState.Unknown)
    {
      ArgumentNullException.ThrowIfNull(kernel);
      ArgumentNullException.ThrowIfNull(driverId);
      ArgumentNullException.ThrowIfNull(containerId);
      _kernel = kernel;
      _logger = kernel.LoggerFactory.CreateLogger<ContainerService>();
      _driverId = driverId;
      _containerId = containerId;
      _image = image;
      _name = name ?? $"container-{containerId}";
      _stopOnDispose = stopOnDispose;
      _deleteOnDispose = deleteOnDispose;
      _deleteVolumeOnDispose = deleteVolumeOnDispose;
      _deleteNamedVolumeOnDispose = deleteNamedVolumeOnDispose;
      _customResolver = customResolver;
      // ponytail: shallow copy detaches the builder-owned list so post-Build list mutation
      // can't corrupt the service's hooks mid-enumeration (7.9); elements are never mutated here.
      _lifecycleHooks = lifecycleHooks is null ? [] : [.. lifecycleHooks];
      _state = initialState;
      _disposeCleanupTimeout =
          disposeCleanupTimeout ?? TimeSpan.FromMilliseconds(DefaultDisposeCleanupTimeoutMs);
      if (_disposeCleanupTimeout <= TimeSpan.Zero)
        throw new ArgumentOutOfRangeException(
            nameof(disposeCleanupTimeout),
            disposeCleanupTimeout,
            "Dispose cleanup timeout must be a positive, finite duration.");
    }

    public string Name => _name;
    public ServiceRunningState State => _state;
    public FluentDockerKernel Kernel => _kernel;
    public string DriverId => _driverId;
    public string Id => _containerId;
    public string Image => _image;

    // IServiceCapabilities
    bool IServiceCapabilities.CanStart => true;
    bool IServiceCapabilities.CanStop => true;
    bool IServiceCapabilities.CanPause => true;
    bool IServiceCapabilities.CanRemove => true;
    bool IServiceCapabilities.CanHook => true;

#pragma warning disable CA1710 // Delegate name 'StateChange' — intentional API design
    public event ServiceDelegates.StateChange StateChange;
#pragma warning restore CA1710

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      ThrowIfDisposed();
      if (_state == ServiceRunningState.Removed)
        throw new InvalidOperationException("Cannot start a removed container.");

      var driver = _kernel.SysCtl<IContainerDriver>(_driverId);
      var context = new DriverContext(_driverId);

      try
      {
        await UpdateStateAndExecuteHooksAsync(ServiceRunningState.Starting).ConfigureAwait(false);

        var response = await driver.StartAsync(context, _containerId, cancellationToken).ConfigureAwait(false);

        if (!response.Success)
        {
          throw new ContainerStartException(
              _containerId,
              response.Error,
              response.ErrorContext,
              response.ErrorCode);
        }

        InvalidateInspectCache();
        var inspect = await driver.InspectAsync(context, _containerId, cancellationToken).ConfigureAwait(false);
        if (inspect == null)
        {
          await UpdateStateAndExecuteHooksAsync(ServiceRunningState.Unknown).ConfigureAwait(false);
          throw new DriverException(
              $"Failed to inspect container '{_name}' after start: empty response",
              ErrorCodes.General.Unknown);
        }
        if (!inspect.Success)
          throw new DriverException(
              $"Failed to inspect container '{_name}' after start: {inspect.Error}",
              inspect.ErrorCode,
              inspect.ErrorContext);

        var inspectedState = ParseInspectState(inspect.Data?.State);
        await UpdateStateAndExecuteHooksAsync(inspectedState).ConfigureAwait(false);
        // Builder orchestrates CopyToOnStart / ExecuteOnRunning once, after wait conditions.
      }
      catch
      {
        await UpdateStateAndExecuteHooksAsync(ServiceRunningState.Unknown).ConfigureAwait(false);
        throw;
      }
    }

    public async Task PauseAsync(CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      ThrowIfDisposed();
      if (_state == ServiceRunningState.Removed)
        throw new InvalidOperationException("Cannot pause a removed container.");
      // No stale-state short-circuit: a cached "Paused" may be wrong (external unpause), so always
      // issue the pause and treat an already-paused daemon response as idempotent success (SVC-MAJ-4).

      var driver = _kernel.SysCtl<IContainerDriver>(_driverId);
      var context = new DriverContext(_driverId);

      try
      {
        var response = await driver.PauseAsync(context, _containerId, cancellationToken).ConfigureAwait(false);

        if (!response.Success && !IsAlreadyPaused(response))
        {
          throw new DriverException(
              $"Failed to pause container '{_name}': {response.Error}",
              response.ErrorCode,
              response.ErrorContext);
        }

        await UpdateStateAndExecuteHooksAsync(ServiceRunningState.Paused).ConfigureAwait(false);
      }
      catch
      {
        await UpdateStateAndExecuteHooksAsync(ServiceRunningState.Unknown).ConfigureAwait(false);
        throw;
      }
    }

    public async Task UnpauseAsync(CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      ThrowIfDisposed();
      if (_state == ServiceRunningState.Removed)
        throw new InvalidOperationException("Cannot unpause a removed container.");

      var driver = _kernel.SysCtl<IContainerDriver>(_driverId);
      var context = new DriverContext(_driverId);

      try
      {
        var response = await driver.UnpauseAsync(context, _containerId, cancellationToken).ConfigureAwait(false);

        if (!response.Success)
        {
          if (!IsAlreadyNotPaused(response))
          {
            throw new DriverException(
                $"Failed to unpause container '{_name}': {response.Error}",
                response.ErrorCode,
                response.ErrorContext);
          }

          // "not paused" also covers stopped/exited containers — inspect for the real state.
          var inspect = await driver.InspectAsync(context, _containerId, cancellationToken).ConfigureAwait(false);
          var actual = inspect?.Success == true
              ? ParseInspectState(inspect.Data?.State)
              : ServiceRunningState.Unknown;
          await UpdateStateAndExecuteHooksAsync(actual).ConfigureAwait(false);
          return;
        }

        await UpdateStateAndExecuteHooksAsync(ServiceRunningState.Running).ConfigureAwait(false);
      }
      catch
      {
        await UpdateStateAndExecuteHooksAsync(ServiceRunningState.Unknown).ConfigureAwait(false);
        throw;
      }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
      await StopCoreAsync(throwIfDisposed: true, cancellationToken).ConfigureAwait(false);
    }

    private async Task StopCoreAsync(bool throwIfDisposed, CancellationToken cancellationToken)
    {
      cancellationToken.ThrowIfCancellationRequested();
      if (throwIfDisposed)
        ThrowIfDisposed();
      // Only short-circuit on the terminal Removed state. A cached "Stopped" may be stale (the
      // container could have been restarted externally / by a restart policy), so we must still
      // issue the stop; the driver maps an already-stopped container to success idempotently
      // (IsAlreadyNotRunning) rather than dropping the intent (SVC-MAJ-4).
      if (_state is ServiceRunningState.Removed)
        return;
      var removeVersion = Volatile.Read(ref _disposeRemoveVersion);

      var driver = _kernel.SysCtl<IContainerDriver>(_driverId);
      var context = new DriverContext(_driverId);

      try
      {
        await UpdateStateAndExecuteHooksAsync(ServiceRunningState.Stopping).ConfigureAwait(false);

        var response = await driver.StopAsync(context, _containerId, null, cancellationToken).ConfigureAwait(false);

        // A container already stopped or externally gone satisfies the stop intent (idempotent).
        if (!response.Success && !IsAlreadyNotRunning(response))
        {
          throw new DriverException(
              $"Failed to stop container '{_name}': {response.Error}",
              response.ErrorCode,
              response.ErrorContext);
        }

        if (removeVersion != Volatile.Read(ref _disposeRemoveVersion))
          return;

        await UpdateStateAndExecuteHooksAsync(ServiceRunningState.Stopped).ConfigureAwait(false);
      }
      catch
      {
        if (removeVersion == Volatile.Read(ref _disposeRemoveVersion))
          await UpdateStateAndExecuteHooksAsync(ServiceRunningState.Unknown).ConfigureAwait(false);
        throw;
      }
    }

    public async Task KillAsync(string signal = "SIGKILL", CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      ThrowIfDisposed();
      if (_state == ServiceRunningState.Removed)
        return;

      var driver = _kernel.SysCtl<IContainerDriver>(_driverId);
      var context = new DriverContext(_driverId);

      try
      {
        await UpdateStateAndExecuteHooksAsync(ServiceRunningState.Stopping).ConfigureAwait(false);

        var response = await driver.KillAsync(context, _containerId, signal, cancellationToken).ConfigureAwait(false);

        // A container already stopped or externally gone satisfies the kill intent (idempotent).
        if (!response.Success && !IsAlreadyNotRunning(response))
        {
          throw new DriverException(
              $"Failed to kill container '{_name}': {response.Error}",
              response.ErrorCode,
              response.ErrorContext);
        }

        // `docker kill` returns on signal DELIVERY, not termination. SIGKILL cannot be caught, so it
        // is guaranteed terminal → Stopped. Any other signal (a handler-ignored SIGTERM, SIGHUP,
        // SIGUSR1) may leave the container running, so inspect for the authoritative state instead of
        // blindly claiming Stopped (SVC-MAJ-1).
        if (IsGuaranteedTerminalSignal(signal))
        {
          await UpdateStateAndExecuteHooksAsync(ServiceRunningState.Stopped).ConfigureAwait(false);
        }
        else
        {
          var inspect = await driver.InspectAsync(context, _containerId, cancellationToken).ConfigureAwait(false);
          var actual = inspect?.Success == true
              ? ParseInspectState(inspect.Data?.State)
              : ServiceRunningState.Unknown;
          await UpdateStateAndExecuteHooksAsync(actual).ConfigureAwait(false);
        }
      }
      catch
      {
        await UpdateStateAndExecuteHooksAsync(ServiceRunningState.Unknown).ConfigureAwait(false);
        throw;
      }
    }

    public async Task RemoveAsync(bool force = false, CancellationToken cancellationToken = default)
    {
      await RemoveCoreAsync(
          force, skipExecuteLifecycleHooks: false, removeVolumesOverride: null,
          throwIfDisposed: true, cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveAsync(
        bool force, bool removeVolumes, CancellationToken cancellationToken = default)
    {
      await RemoveCoreAsync(
          force, skipExecuteLifecycleHooks: false, removeVolumes,
          throwIfDisposed: true, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Container> InspectAsync(CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      ThrowIfDisposed();
      // Return cached result if still valid (reduces redundant calls during wait polling).
      // Single volatile reference read ensures data and timestamp are always consistent.
      var entry = _inspectCacheEntry;
      var now = Stopwatch.GetTimestamp();
      if (entry != null &&
          Stopwatch.GetElapsedTime(entry.Timestamp, now).TotalMilliseconds < InspectCacheTtlMs)
      {
        return entry.Data;
      }

      // Capture the cache version before the async call. If a state change
      // occurs during the fetch, the version will have incremented and we
      // must not store the now-stale result in the cache.
      var versionBefore = _cacheVersion;
      var inspectSequence = Interlocked.Increment(ref _inspectSequence);

      var driver = _kernel.SysCtl<IContainerDriver>(_driverId);
      var context = new DriverContext(_driverId);

      var response = await driver.InspectAsync(context, _containerId, cancellationToken).ConfigureAwait(false);

      if (!response.Success)
      {
        if (IsContainerAlreadyGone(response))
          await UpdateStateAndExecuteHooksAsync(ServiceRunningState.Removed).ConfigureAwait(false);
        throw new DriverException(
            $"Failed to inspect container '{_name}': {response.Error}",
            response.ErrorCode,
            response.ErrorContext);
      }

      await ApplyInspectResultIfVersionCurrentAsync(versionBefore, inspectSequence, response.Data)
          .ConfigureAwait(false);

      return response.Data;
    }

    private async Task ApplyInspectResultIfVersionCurrentAsync(int versionBefore, int inspectSequence, Container data)
    {
      ServiceDelegates.StateChange stateChange = null;
      StateChangeEventArgs args = null;
      ServiceRunningState? changedState = null;
      lock (_stateLock)
      {
        if (Volatile.Read(ref _disposeCompleted) != 0 ||
            versionBefore != _cacheVersion ||
            inspectSequence < _lastAppliedInspectSequence)
          return;

        _lastAppliedInspectSequence = inspectSequence;
        if (data?.State != null)
        {
          var newState = ParseInspectState(data.State);
          if (_state != newState)
          {
            _state = newState;
            changedState = newState;
            stateChange = StateChange;
            args = stateChange == null ? null : new StateChangeEventArgs(this, newState);
          }
        }

        _inspectCacheEntry = new InspectCacheEntry(data, Stopwatch.GetTimestamp());
      }

      if (stateChange != null)
        StateChangeNotifier.Invoke(stateChange, args, _logger, "ContainerService");

      if (changedState.HasValue)
        await ExecuteHooksAsync(changedState.Value).ConfigureAwait(false);
    }

    private static ServiceRunningState ParseInspectState(ContainerState? state) =>
        state?.Running == true && state.Paused != true && state.Restarting != true
            ? ServiceRunningState.Running
            : ParseState(state?.Status);

    // ponytail: key on _disposeCompleted (dispose finished), not _disposed (dispose started), so
    // lifecycle hooks firing DURING dispose can still observe the live container (7.5/M1); external
    // callers after Dispose() returns still get ObjectDisposedException.
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeCompleted) != 0, this);

  }
}
