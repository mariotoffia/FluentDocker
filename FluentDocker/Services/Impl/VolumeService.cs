using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Volumes;

#pragma warning disable CS0618 // IService obsolete — intentional usage

namespace FluentDocker.Services.Impl
{
  /// <summary>
  /// Volume service implementation using kernel and driver.
  /// </summary>
  public class VolumeService : IVolumeService, IServiceCapabilities
  {
    // IServiceCapabilities
    bool IServiceCapabilities.CanStart => false;
    bool IServiceCapabilities.CanStop => false;
    bool IServiceCapabilities.CanPause => false;
    bool IServiceCapabilities.CanRemove => true;

    private readonly FluentDockerKernel _kernel;
    private readonly string _driverId;
    private readonly string _volumeName;
    private readonly string _driver;
    private readonly bool _removeOnDispose;
    private readonly Dictionary<string, Func<IServiceAsync, Task>> _hooks = new Dictionary<string, Func<IServiceAsync, Task>>();
    private ServiceRunningState _state = ServiceRunningState.Running;

    public VolumeService(
        FluentDockerKernel kernel,
        string driverId,
        string volumeName,
        string driver,
        bool removeOnDispose = false)
    {
      ArgumentNullException.ThrowIfNull(kernel);
      ArgumentNullException.ThrowIfNull(driverId);
      ArgumentNullException.ThrowIfNull(volumeName);
      _kernel = kernel;
      _driverId = driverId;
      _volumeName = volumeName;
      _driver = driver ?? "local";
      _removeOnDispose = removeOnDispose;
    }

    public string Name => _volumeName;
    public ServiceRunningState State => _state;
    public FluentDockerKernel Kernel => _kernel;
    public string DriverId => _driverId;
    public string VolumeName => _volumeName;
    public string Driver => _driver;

#pragma warning disable CA1710 // Delegate name 'StateChange' — intentional API design
    public event ServiceDelegates.StateChange StateChange;
#pragma warning restore CA1710

    public async Task<Volume> InspectAsync(CancellationToken cancellationToken = default)
    {
      var driver = _kernel.SysCtl<IVolumeDriver>(_driverId);
      var context = new DriverContext(_driverId);

      var response = await driver.InspectAsync(context, _volumeName, cancellationToken).ConfigureAwait(false);

      if (!response.Success)
      {
        throw new DriverException(
            $"Failed to inspect volume '{_volumeName}': {response.Error}",
            response.ErrorCode,
            response.ErrorContext);
      }

      return response.Data;
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
      return Task.CompletedTask;
    }

    public Task PauseAsync(CancellationToken cancellationToken = default)
    {
      throw new NotSupportedException("Volumes cannot be paused");
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
      throw new NotSupportedException("Volumes cannot be stopped, use RemoveAsync instead");
    }

    public async Task RemoveAsync(bool force = false, CancellationToken cancellationToken = default)
    {
      var driver = _kernel.SysCtl<IVolumeDriver>(_driverId);
      var context = new DriverContext(_driverId);

      var response = await driver.RemoveAsync(context, _volumeName, force, cancellationToken).ConfigureAwait(false);

      if (!response.Success)
      {
        throw new DriverException(
            $"Failed to remove volume '{_volumeName}': {response.Error}",
            response.ErrorCode,
            response.ErrorContext);
      }

      UpdateState(ServiceRunningState.Removed);
      await ExecuteHooksAsync(ServiceRunningState.Removed).ConfigureAwait(false);
    }

    public IServiceAsync AddHook(ServiceRunningState state, Func<IServiceAsync, Task> hook, string uniqueName = null)
    {
      var name = uniqueName ?? Guid.NewGuid().ToString();
      _hooks[name] = hook;
      return this;
    }

    public IServiceAsync RemoveHook(string uniqueName)
    {
      _hooks.Remove(uniqueName);
      return this;
    }

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

    private int _disposed;

    public void Dispose()
    {
      if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        return;
      DisposeCoreAsync().AsTask().GetAwaiter().GetResult();
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
      if (!_removeOnDispose)
        return;

      try
      {
        await RemoveAsync(force: true).ConfigureAwait(false);
      }
      catch (Exception ex)
      {
        Logger.Log($"VolumeService DisposeAsync failed: {ex.Message}");
      }
    }

    private void UpdateState(ServiceRunningState newState)
    {
      _state = newState;
      StateChange?.Invoke(this, new StateChangeEventArgs(this, newState));
    }

    private async Task ExecuteHooksAsync(ServiceRunningState state)
    {
      foreach (var hook in _hooks.Values)
      {
        try
        {
          await hook(this).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
          Logger.Log($"VolumeService hook execution failed: {ex.Message}");
        }
      }
    }
  }
}

