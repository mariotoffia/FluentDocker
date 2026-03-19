using System;
using System.Collections.Generic;
using System.Diagnostics;
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

#pragma warning disable CS0618 // IService obsolete — intentional usage

namespace FluentDocker.Services.Impl
{
  /// <summary>
  /// Container service implementation using kernel and driver.
  /// </summary>
  public partial class ContainerService : IContainerService
  {
    private readonly FluentDockerKernel _kernel;
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
    private readonly Dictionary<string, Func<IServiceAsync, Task>> _hooks = new Dictionary<string, Func<IServiceAsync, Task>>();
    private readonly Dictionary<ServiceRunningState, List<Func<IServiceAsync, Task>>> _stateHooks =
        new Dictionary<ServiceRunningState, List<Func<IServiceAsync, Task>>>();
    private ServiceRunningState _state = ServiceRunningState.Unknown;

    // Short-lived inspect cache to avoid redundant API/CLI calls during wait polling.
    // Thread-safety: Volatile/Interlocked used so concurrent wait-condition polls see
    // consistent state without a full lock.
    private volatile Container? _inspectCache;
    private long _inspectCacheTimestamp; // accessed via Interlocked

    /// <summary>
    /// Time-to-live in milliseconds for the InspectAsync result cache.
    /// </summary>
    public const long InspectCacheTtlMs = 500;

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
        List<LifecycleHook> lifecycleHooks = null)
    {
      ArgumentNullException.ThrowIfNull(kernel);
      ArgumentNullException.ThrowIfNull(driverId);
      ArgumentNullException.ThrowIfNull(containerId);
      _kernel = kernel;
      _driverId = driverId;
      _containerId = containerId;
      _image = image;
      _name = name ?? $"container-{containerId}";
      _stopOnDispose = stopOnDispose;
      _deleteOnDispose = deleteOnDispose;
      _deleteVolumeOnDispose = deleteVolumeOnDispose;
      _deleteNamedVolumeOnDispose = deleteNamedVolumeOnDispose;
      _customResolver = customResolver;
      _lifecycleHooks = lifecycleHooks ?? new List<LifecycleHook>();

      // Initialize state hook lists
      foreach (var state in Enum.GetValues<ServiceRunningState>())
      {
        _stateHooks[state] = new List<Func<IServiceAsync, Task>>();
      }
    }

    public string Name => _name;
    public ServiceRunningState State => _state;
    public FluentDockerKernel Kernel => _kernel;
    public string DriverId => _driverId;
    public string Id => _containerId;
    public string Image => _image;

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

      UpdateState(ServiceRunningState.Running);
      await ExecuteHooksAsync(ServiceRunningState.Running).ConfigureAwait(false);
      await ExecuteLifecycleHooksAsync(ServiceRunningState.Running, cancellationToken).ConfigureAwait(false);
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
      // Uses volatile read for _inspectCache and Interlocked for _inspectCacheTimestamp
      // so concurrent callers see a consistent snapshot without locking.
      var cached = _inspectCache;
      var now = Stopwatch.GetTimestamp();
      if (cached != null &&
          Stopwatch.GetElapsedTime(Interlocked.Read(ref _inspectCacheTimestamp), now).TotalMilliseconds < InspectCacheTtlMs)
      {
        return cached;
      }

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

      // Store result and timestamp atomically relative to each other:
      // write timestamp first, then the reference (volatile write is release-fence).
      Interlocked.Exchange(ref _inspectCacheTimestamp, Stopwatch.GetTimestamp());
      _inspectCache = response.Data;

      return response.Data;
    }

    /// <summary>
    /// Invalidates the inspect cache so the next InspectAsync call fetches fresh data.
    /// Called automatically by state-changing operations (Start, Stop, Pause, Remove).
    /// </summary>
    private void InvalidateInspectCache()
    {
      _inspectCache = null;
      Interlocked.Exchange(ref _inspectCacheTimestamp, 0);
    }

    #region Hooks

    public IServiceAsync AddHook(ServiceRunningState state, Func<IServiceAsync, Task> hook, string uniqueName = null)
    {
      var name = uniqueName ?? Guid.NewGuid().ToString();
      _hooks[name] = hook;
      _stateHooks[state].Add(hook);
      return this;
    }

    public IServiceAsync RemoveHook(string uniqueName)
    {
      if (_hooks.TryGetValue(uniqueName, out var hook))
      {
        _hooks.Remove(uniqueName);
        foreach (var stateList in _stateHooks.Values)
        {
          stateList.Remove(hook);
        }
      }
      return this;
    }

    #endregion

    #region IService Sync Implementations

    void IService.Start() => StartAsync().GetAwaiter().GetResult();
    void IService.Pause() => PauseAsync().GetAwaiter().GetResult();
    void IService.Stop() => StopAsync().GetAwaiter().GetResult();
    void IService.Remove(bool force) => RemoveAsync(force).GetAwaiter().GetResult();

    IService IService.AddHook(ServiceRunningState state, Action<IService> hook, string uniqueName)
    {
      return AddHook(state, async service => hook(service), uniqueName);
    }

    IService IService.RemoveHook(string uniqueName)
    {
      RemoveHook(uniqueName);
      return this;
    }

    #endregion

    #region Dispose

    public void Dispose()
    {
      DisposeAsync().AsTask().GetAwaiter().GetResult();
      GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
      try
      {
        if (_stopOnDispose &&
            (_state == ServiceRunningState.Running || _state == ServiceRunningState.Paused))
        {
          await StopAsync().ConfigureAwait(false);
        }

        if (_deleteOnDispose)
        {
          await RemoveAsync(force: true).ConfigureAwait(false);
        }
      }
      catch (Exception ex)
      {
        Logger.Log($"ContainerService DisposeAsync failed: {ex.Message}");
      }

      GC.SuppressFinalize(this);
    }

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
      if (!_stateHooks.TryGetValue(state, out var hooks))
        return;

      foreach (var hook in hooks)
      {
        try
        {
          await hook(this).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
          Logger.Log($"ContainerService hook execution failed: {ex.Message}");
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
                  // Extract tar to directory
                  // Simplified - would need proper tar extraction
                  await File.WriteAllBytesAsync(hook.HostPath + ".tar", exportData, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                  await File.WriteAllBytesAsync(hook.HostPath, exportData, cancellationToken).ConfigureAwait(false);
                }
              }
              break;

            case LifecycleHookType.Execute:
              if (hook.Command?.Length > 0)
              {
                await ExecuteAsync(string.Join(" ", hook.Command), cancellationToken).ConfigureAwait(false);
              }
              break;
          }
        }
        catch (Exception ex)
        {
          // Log but don't fail on lifecycle hook errors
          Logger.Log($"Lifecycle hook failed: {ex.Message}");
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
