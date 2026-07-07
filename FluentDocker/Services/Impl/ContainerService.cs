using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO;
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
      _lifecycleHooks = lifecycleHooks ?? [];
      _state = initialState;
      _disposeCleanupTimeout =
          disposeCleanupTimeout ?? TimeSpan.FromMilliseconds(DefaultDisposeCleanupTimeoutMs);
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
      ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
      if (_state == ServiceRunningState.Removed)
        throw new ObjectDisposedException(Name, "Cannot start a removed container.");

      var driver = _kernel.SysCtl<IContainerDriver>(_driverId);
      var context = new DriverContext(_driverId);

      try
      {
        UpdateState(ServiceRunningState.Starting);
        await ExecuteHooksAsync(ServiceRunningState.Starting).ConfigureAwait(false);

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
          UpdateState(ServiceRunningState.Unknown);
          throw new DriverException(
              $"Failed to inspect container '{_name}' after start: empty response",
              ErrorCodes.General.Unknown);
        }
        if (!inspect.Success)
          throw new DriverException(
              $"Failed to inspect container '{_name}' after start: {inspect.Error}",
              inspect.ErrorCode,
              inspect.ErrorContext);

        var inspectedState = inspect.Data?.State?.Running == true
            ? ServiceRunningState.Running
            : ParseState(inspect.Data?.State?.Status);
        UpdateState(inspectedState);
        if (_state == ServiceRunningState.Running)
          await ExecuteHooksAsync(ServiceRunningState.Running).ConfigureAwait(false);
        // Builder orchestrates CopyToOnStart / ExecuteOnRunning once, after wait conditions.
      }
      catch
      {
        UpdateState(ServiceRunningState.Unknown);
        throw;
      }
    }

    public async Task PauseAsync(CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      if (_state == ServiceRunningState.Paused)
        return;

      var driver = _kernel.SysCtl<IContainerDriver>(_driverId);
      var context = new DriverContext(_driverId);

      var response = await driver.PauseAsync(context, _containerId, cancellationToken).ConfigureAwait(false);

      if (!response.Success)
      {
        throw new DriverException(
            $"Failed to pause container '{_name}': {response.Error}",
            response.ErrorCode,
            response.ErrorContext);
      }

      UpdateState(ServiceRunningState.Paused);
      await ExecuteHooksAsync(ServiceRunningState.Paused).ConfigureAwait(false);
    }

    public async Task UnpauseAsync(CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      if (_state == ServiceRunningState.Removed)
        return;

      var driver = _kernel.SysCtl<IContainerDriver>(_driverId);
      var context = new DriverContext(_driverId);

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
            ? (inspect.Data?.State?.Running == true
                ? ServiceRunningState.Running
                : ParseState(inspect.Data?.State?.Status))
            : ServiceRunningState.Unknown;
        UpdateState(actual);
        await ExecuteHooksAsync(actual).ConfigureAwait(false);
        return;
      }

      UpdateState(ServiceRunningState.Running);
      await ExecuteHooksAsync(ServiceRunningState.Running).ConfigureAwait(false);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      if (_state == ServiceRunningState.Removed)
        return;

      var driver = _kernel.SysCtl<IContainerDriver>(_driverId);
      var context = new DriverContext(_driverId);

      try
      {
        UpdateState(ServiceRunningState.Stopping);
        await ExecuteHooksAsync(ServiceRunningState.Stopping).ConfigureAwait(false);

        var response = await driver.StopAsync(context, _containerId, null, cancellationToken).ConfigureAwait(false);

        // A container already stopped or externally gone satisfies the stop intent (idempotent).
        if (!response.Success && !IsAlreadyNotRunning(response))
        {
          throw new DriverException(
              $"Failed to stop container '{_name}': {response.Error}",
              response.ErrorCode,
              response.ErrorContext);
        }

        UpdateState(ServiceRunningState.Stopped);
        await ExecuteHooksAsync(ServiceRunningState.Stopped).ConfigureAwait(false);
      }
      catch
      {
        UpdateState(ServiceRunningState.Unknown);
        throw;
      }
    }

    public async Task KillAsync(string signal = "SIGKILL", CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      if (_state == ServiceRunningState.Removed)
        return;

      var driver = _kernel.SysCtl<IContainerDriver>(_driverId);
      var context = new DriverContext(_driverId);

      try
      {
        UpdateState(ServiceRunningState.Stopping);
        await ExecuteHooksAsync(ServiceRunningState.Stopping).ConfigureAwait(false);

        var response = await driver.KillAsync(context, _containerId, signal, cancellationToken).ConfigureAwait(false);

        // A container already stopped or externally gone satisfies the kill intent (idempotent).
        if (!response.Success && !IsAlreadyNotRunning(response))
        {
          throw new DriverException(
              $"Failed to kill container '{_name}': {response.Error}",
              response.ErrorCode,
              response.ErrorContext);
        }

        UpdateState(ServiceRunningState.Stopped);
        await ExecuteHooksAsync(ServiceRunningState.Stopped).ConfigureAwait(false);
      }
      catch
      {
        UpdateState(ServiceRunningState.Unknown);
        throw;
      }
    }

    public async Task RemoveAsync(bool force = false, CancellationToken cancellationToken = default)
    {
      await RemoveCoreAsync(force, skipExecuteLifecycleHooks: false, null, cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveAsync(
        bool force, bool removeVolumes, CancellationToken cancellationToken = default)
    {
      await RemoveCoreAsync(
          force, skipExecuteLifecycleHooks: false, removeVolumes, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Container> InspectAsync(CancellationToken cancellationToken = default)
    {
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

      var driver = _kernel.SysCtl<IContainerDriver>(_driverId);
      var context = new DriverContext(_driverId);

      var response = await driver.InspectAsync(context, _containerId, cancellationToken).ConfigureAwait(false);

      if (!response.Success)
      {
        throw new DriverException(
            $"Failed to inspect container '{_name}': {response.Error}",
            response.ErrorCode,
            response.ErrorContext);
      }

      // Update state from inspection
      if (response.Data?.State != null)
      {
        UpdateStateFromInspect(ParseState(response.Data.State.Status));
      }

      // Only cache the result if no state change occurred during the fetch.
      if (versionBefore == _cacheVersion)
      {
        _inspectCacheEntry = new InspectCacheEntry(response.Data, Stopwatch.GetTimestamp());
      }

      return response.Data;
    }

  }
}
