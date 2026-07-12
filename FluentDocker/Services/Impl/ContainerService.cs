using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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

    private int _disposeRemoveVersion;

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

    /// <inheritdoc />
    public string Name => _name;

    /// <inheritdoc />
    public ServiceRunningState State => _state;

    /// <inheritdoc />
    public FluentDockerKernel Kernel => _kernel;

    /// <inheritdoc />
    public string DriverId => _driverId;

    /// <inheritdoc />
    public string Id => _containerId;

    /// <inheritdoc />
    public string Image => _image;

    // IServiceCapabilities
    bool IServiceCapabilities.CanStart => true;
    bool IServiceCapabilities.CanStop => true;
    bool IServiceCapabilities.CanPause => true;
    bool IServiceCapabilities.CanRemove => true;
    bool IServiceCapabilities.CanHook => true;

#pragma warning disable CA1710 // Delegate name 'StateChange' — intentional API design
    /// <inheritdoc />
    public event ServiceDelegates.StateChange StateChange;
#pragma warning restore CA1710

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
    public async Task RemoveAsync(bool force = false, CancellationToken cancellationToken = default)
    {
      await RemoveCoreAsync(
          force, skipExecuteLifecycleHooks: false, removeVolumesOverride: null,
          throwIfDisposed: true, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Removes the container, overriding the constructor's <c>deleteVolumeOnDispose</c> choice for
    /// anonymous volume removal on this call only.
    /// </summary>
    /// <param name="force">When true, removes a running container without stopping it first.</param>
    /// <param name="removeVolumes">When true, also removes anonymous volumes owned by the container.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task RemoveAsync(
        bool force, bool removeVolumes, CancellationToken cancellationToken = default)
    {
      await RemoveCoreAsync(
          force, skipExecuteLifecycleHooks: false, removeVolumes,
          throwIfDisposed: true, cancellationToken).ConfigureAwait(false);
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
