using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers.Podman;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Services.Impl
{
  /// <summary>
  /// Pod service implementation using kernel and Podman pod driver.
  /// </summary>
  public class PodService : IPodService, IServiceCapabilities
  {
    // IServiceCapabilities
    bool IServiceCapabilities.CanStart => true;
    bool IServiceCapabilities.CanStop => true;
    bool IServiceCapabilities.CanPause => false;
    bool IServiceCapabilities.CanRemove => true;

    private readonly FluentDockerKernel _kernel;
    private readonly string _driverId;
    private readonly string _podName;
    private readonly string _podId;
    private readonly bool _removeOnDispose;
    private readonly Dictionary<string, Func<IServiceAsync, Task>> _hooks = new();
    private ServiceRunningState _state = ServiceRunningState.Stopped;

    public PodService(
        FluentDockerKernel kernel, string driverId,
        string podId, string podName, bool removeOnDispose = false)
    {
      ArgumentNullException.ThrowIfNull(kernel);
      ArgumentNullException.ThrowIfNull(driverId);
      ArgumentNullException.ThrowIfNull(podId);
      _kernel = kernel;
      _driverId = driverId;
      _podId = podId;
      _podName = podName ?? podId;
      _removeOnDispose = removeOnDispose;
    }

    public string Name => _podName;
    public string Id => _podId;
    public ServiceRunningState State => _state;
    public FluentDockerKernel Kernel => _kernel;
    public string DriverId => _driverId;

#pragma warning disable CA1710 // Delegate name 'StateChange' — intentional API design
    public event ServiceDelegates.StateChange StateChange;
#pragma warning restore CA1710

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
      var driver = _kernel.SysCtl<IPodmanPodDriver>(_driverId);
      var context = new DriverContext(_driverId);
      var response = await driver.StartPodAsync(context, _podName, cancellationToken).ConfigureAwait(false);
      if (response.Success)
        UpdateState(ServiceRunningState.Running);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
      var driver = _kernel.SysCtl<IPodmanPodDriver>(_driverId);
      var context = new DriverContext(_driverId);
      var response = await driver.StopPodAsync(context, _podName, 10, cancellationToken).ConfigureAwait(false);
      if (response.Success)
        UpdateState(ServiceRunningState.Stopped);
    }

    public Task PauseAsync(CancellationToken cancellationToken = default)
    {
      throw new NotSupportedException("Pods cannot be paused via builder");
    }

    public async Task RemoveAsync(
        bool force = false, CancellationToken cancellationToken = default)
    {
      var driver = _kernel.SysCtl<IPodmanPodDriver>(_driverId);
      var context = new DriverContext(_driverId);
      var response = await driver.RemovePodAsync(
          context, _podName, force, cancellationToken).ConfigureAwait(false);
      if (response.Success)
        UpdateState(ServiceRunningState.Removed);
    }

    public IServiceAsync AddHook(
        ServiceRunningState state, Func<IServiceAsync, Task> hook, string uniqueName = null)
    {
      _hooks[uniqueName ?? Guid.NewGuid().ToString()] = hook;
      return this;
    }

    public IServiceAsync RemoveHook(string uniqueName)
    {
      _hooks.Remove(uniqueName);
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
      { await RemoveAsync(force: true).ConfigureAwait(false); }
      catch (Exception ex) { Logger.Log($"PodService DisposeAsync failed: {ex.Message}"); }
    }

    private void UpdateState(ServiceRunningState newState)
    {
      _state = newState;
      StateChange?.Invoke(this, new StateChangeEventArgs(this, newState));
    }
  }
}
