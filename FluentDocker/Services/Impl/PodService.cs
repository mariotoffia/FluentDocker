using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers.Podman;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;

#pragma warning disable CS0618 // IService obsolete — intentional usage

namespace FluentDocker.Services.Impl
{
  /// <summary>
  /// Pod service implementation using kernel and Podman pod driver.
  /// </summary>
  public class PodService : IPodService
  {
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
      var response = await driver.StartPodAsync(context, _podName, cancellationToken);
      if (response.Success)
        UpdateState(ServiceRunningState.Running);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
      var driver = _kernel.SysCtl<IPodmanPodDriver>(_driverId);
      var context = new DriverContext(_driverId);
      var response = await driver.StopPodAsync(context, _podName, 10, cancellationToken);
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
          context, _podName, force, cancellationToken);
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

    void IService.Start() => StartAsync().GetAwaiter().GetResult();
    void IService.Pause() => PauseAsync().GetAwaiter().GetResult();
    void IService.Stop() => StopAsync().GetAwaiter().GetResult();
    void IService.Remove(bool force) => RemoveAsync(force).GetAwaiter().GetResult();

    IService IService.AddHook(
        ServiceRunningState state, Action<IService> hook, string uniqueName)
        => AddHook(state, async service => hook(service), uniqueName);

    IService IService.RemoveHook(string uniqueName)
    {
      RemoveHook(uniqueName);
      return this;
    }

    public void Dispose()
    {
      DisposeAsync().AsTask().GetAwaiter().GetResult();
      GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
      if (!_removeOnDispose)
      {
        GC.SuppressFinalize(this);
        return;
      }

      try
      { await RemoveAsync(force: true); }
      catch (Exception ex) { Logger.Log($"PodService DisposeAsync failed: {ex.Message}"); }
      GC.SuppressFinalize(this);
    }

    private void UpdateState(ServiceRunningState newState)
    {
      _state = newState;
      StateChange?.Invoke(this, new StateChangeEventArgs(this, newState));
    }
  }
}
