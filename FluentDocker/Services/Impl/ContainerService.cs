using System;
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
  /// <summary>
  /// Container service implementation using kernel and driver.
  /// </summary>
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
    private readonly Dictionary<string, (ServiceRunningState State, Func<IServiceAsync, Task> Hook)> _hooks = [];
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
        TimeSpan? disposeCleanupTimeout = null)
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

#pragma warning disable CA1710 // Delegate name 'StateChange' — intentional API design
    public event ServiceDelegates.StateChange StateChange;
#pragma warning restore CA1710

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
      var driver = _kernel.SysCtl<IContainerDriver>(_driverId);
      var context = new DriverContext(_driverId);

      UpdateState(ServiceRunningState.Starting);
      await ExecuteHooksAsync(ServiceRunningState.Starting).ConfigureAwait(false);

      var response = await driver.StartAsync(context, _containerId, cancellationToken).ConfigureAwait(false);

      if (!response.Success)
      {
        throw new ContainerStartException(
            _containerId,
            response.Error,
            response.ErrorContext);
      }

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

      UpdateState(inspect.Data?.State?.Running == true
          ? ServiceRunningState.Running
          : ParseState(inspect.Data?.State?.Status));
      if (inspect.Data != null)
        _inspectCacheEntry = new InspectCacheEntry(inspect.Data, Stopwatch.GetTimestamp());
      if (_state == ServiceRunningState.Running)
        await ExecuteHooksAsync(ServiceRunningState.Running).ConfigureAwait(false);
      // Builder orchestrates CopyToOnStart / ExecuteOnRunning once, after wait conditions.
    }

    public async Task PauseAsync(CancellationToken cancellationToken = default)
    {
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

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
      var driver = _kernel.SysCtl<IContainerDriver>(_driverId);
      var context = new DriverContext(_driverId);

      UpdateState(ServiceRunningState.Stopping);
      await ExecuteHooksAsync(ServiceRunningState.Stopping).ConfigureAwait(false);

      var response = await driver.StopAsync(context, _containerId, null, cancellationToken).ConfigureAwait(false);

      if (!response.Success)
      {
        throw new DriverException(
            $"Failed to stop container '{_name}': {response.Error}",
            response.ErrorCode,
            response.ErrorContext);
      }

      UpdateState(ServiceRunningState.Stopped);
      await ExecuteHooksAsync(ServiceRunningState.Stopped).ConfigureAwait(false);
    }

    public async Task KillAsync(string signal = "SIGKILL", CancellationToken cancellationToken = default)
    {
      var driver = _kernel.SysCtl<IContainerDriver>(_driverId);
      var context = new DriverContext(_driverId);

      UpdateState(ServiceRunningState.Stopping);
      await ExecuteHooksAsync(ServiceRunningState.Stopping).ConfigureAwait(false);

      var response = await driver.KillAsync(context, _containerId, signal, cancellationToken).ConfigureAwait(false);

      if (!response.Success)
      {
        throw new DriverException(
            $"Failed to kill container '{_name}': {response.Error}",
            response.ErrorCode,
            response.ErrorContext);
      }

      UpdateState(ServiceRunningState.Stopped);
      await ExecuteHooksAsync(ServiceRunningState.Stopped).ConfigureAwait(false);
    }

    public async Task RemoveAsync(bool force = false, CancellationToken cancellationToken = default)
    {
      var driver = _kernel.SysCtl<IContainerDriver>(_driverId);
      var context = new DriverContext(_driverId);

      UpdateState(ServiceRunningState.Removing);
      await ExecuteHooksAsync(ServiceRunningState.Removing).ConfigureAwait(false);
      await ExecuteLifecycleHooksAsync(ServiceRunningState.Removing, cancellationToken).ConfigureAwait(false);

      var removeVolumes = _deleteVolumeOnDispose || _deleteNamedVolumeOnDispose;
      var response = await driver.RemoveAsync(context, _containerId, force, removeVolumes, cancellationToken).ConfigureAwait(false);

      if (!response.Success)
      {
        throw new DriverException(
            $"Failed to remove container '{_name}': {response.Error}",
            response.ErrorCode,
            response.ErrorContext);
      }

      UpdateState(ServiceRunningState.Removed);
      await ExecuteHooksAsync(ServiceRunningState.Removed).ConfigureAwait(false);
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
        _state = ParseState(response.Data.State.Status);
      }

      // Only cache the result if no state change occurred during the fetch.
      if (versionBefore == _cacheVersion)
      {
        _inspectCacheEntry = new InspectCacheEntry(response.Data, Stopwatch.GetTimestamp());
      }

      return response.Data;
    }

    /// <summary>
    /// Invalidates the inspect cache so the next InspectAsync call fetches fresh data.
    /// Called automatically by state-changing operations (Start, Stop, Pause, Remove).
    /// Incrementing _cacheVersion also prevents any in-flight InspectAsync from
    /// storing its now-stale result.
    /// </summary>
    private void InvalidateInspectCache()
    {
      Interlocked.Increment(ref _cacheVersion);
      _inspectCacheEntry = null;
    }

    #region Hooks

    public IServiceAsync AddHook(ServiceRunningState state, Func<IServiceAsync, Task> hook, string uniqueName = null)
    {
      var name = uniqueName ?? Guid.NewGuid().ToString();
      _hooks[name] = (state, hook);
      return this;
    }

    public IServiceAsync RemoveHook(string uniqueName)
    {
      _hooks.Remove(uniqueName);
      return this;
    }

    #endregion

    #region Dispose

    private int _disposed;

    public void Dispose()
    {
      if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        return;
      // Dispatched to the thread pool to avoid sync-over-async deadlocks.
      Task.Run(() => DisposeCoreAsync().AsTask()).GetAwaiter().GetResult();
      GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
      if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        return;
      await DisposeCoreAsync().ConfigureAwait(false);
      GC.SuppressFinalize(this);
    }

    private async ValueTask DisposeCoreAsync()
    {
      if (!_stopOnDispose && !_deleteOnDispose)
        return;

      // Bound the stop/remove cleanup so a hung or unresponsive daemon cannot block disposal
      // indefinitely. WaitAsync enforces the bound even if the underlying driver call ignores
      // cancellation; the abandoned operation continues in the background but disposal returns.
      using var cleanupCts = new CancellationTokenSource(_disposeCleanupTimeout);

      if (_stopOnDispose &&
          (_state == ServiceRunningState.Running || _state == ServiceRunningState.Paused))
      {
        var stopTask = StopAsync(cleanupCts.Token);
        try
        {
          await stopTask.WaitAsync(cleanupCts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
          _logger.LogWarning(ex, "ContainerService stop on dispose failed");
          ObserveAbandonedCleanup(stopTask);
        }
      }

      if (_deleteOnDispose)
      {
        var removeTask = RemoveAsync(force: true, cleanupCts.Token);
        try
        {
          await removeTask.WaitAsync(cleanupCts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
          _logger.LogWarning(ex, "ContainerService remove on dispose failed");
          ObserveAbandonedCleanup(removeTask);
        }
      }
    }

    // Trade-off: a driver that ignores cancellation keeps running after the WaitAsync timeout;
    // we attach a fault-swallowing continuation so its eventual fault is observed and cannot
    // resurface as an UnobservedTaskException mutating this already-disposed object.
    private static void ObserveAbandonedCleanup(Task task) =>
        _ = task.ContinueWith(
            static t => _ = t.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);

    #endregion

    #region Private Methods

    private void UpdateState(ServiceRunningState newState)
    {
      var oldState = _state;
      _state = newState;
      InvalidateInspectCache();
      StateChange?.Invoke(this, new StateChangeEventArgs(this, newState));
    }

    private async Task ExecuteHooksAsync(ServiceRunningState state)
    {
      // Snapshot: a firing hook may add or remove hooks, which would invalidate a live enumerator.
      foreach (var entry in new List<(ServiceRunningState State, Func<IServiceAsync, Task> Hook)>(_hooks.Values))
      {
        if (entry.State != state)
          continue;

        try
        {
          await entry.Hook(this).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
          _logger.LogError(ex, "ContainerService hook execution failed");
        }
      }
    }

    private async Task ExecuteLifecycleHooksAsync(ServiceRunningState state, CancellationToken cancellationToken)
    {
      foreach (var hook in _lifecycleHooks)
      {
        if (hook.TriggerState != state)
          continue;

        try
        {
          switch (hook.Type)
          {
            case LifecycleHookType.CopyTo:
              if (File.Exists(hook.HostPath) || Directory.Exists(hook.HostPath))
              {
                // Use path-based copy which supports both files and directories
                await CopyToAsync(hook.HostPath, hook.ContainerPath, cancellationToken).ConfigureAwait(false);
              }
              break;

            case LifecycleHookType.CopyFrom:
              // Use path-based copy which supports both files and directories
              await CopyFromToPathAsync(hook.ContainerPath, hook.HostPath, cancellationToken).ConfigureAwait(false);
              break;

            case LifecycleHookType.Export:
              if (hook.Condition == null || hook.Condition(this))
              {
                var exportData = await ExportAsync(cancellationToken).ConfigureAwait(false);
                var exportDir = Path.GetDirectoryName(hook.HostPath);
                if (!string.IsNullOrEmpty(exportDir) && !Directory.Exists(exportDir))
                  Directory.CreateDirectory(exportDir);

                if (hook.Explode)
                {
                  Directory.CreateDirectory(hook.HostPath);
                  using var stream = new MemoryStream(exportData);
                  TarFile.ExtractToDirectory(stream, hook.HostPath, overwriteFiles: true);
                }
                else
                {
                  await File.WriteAllBytesAsync(hook.HostPath, exportData, cancellationToken).ConfigureAwait(false);
                }
              }
              break;

            case LifecycleHookType.Execute:
              if (hook.Command != null)
                await ExecuteAsync(hook.Command, cancellationToken).ConfigureAwait(false);
              break;
          }
        }
        catch (Exception ex)
        {
          _logger.LogError(ex, "Lifecycle hook failed");
          if (hook.Type == LifecycleHookType.Export && hook.Explode)
            throw;
        }
      }
    }

    private static ServiceRunningState ParseState(string state)
    {
      return state?.ToLower() switch
      {
        "running" => ServiceRunningState.Running,
        "paused" => ServiceRunningState.Paused,
        "exited" => ServiceRunningState.Stopped,
        "created" => ServiceRunningState.Starting,
        _ => ServiceRunningState.Unknown
      };
    }

    #endregion
  }
}
