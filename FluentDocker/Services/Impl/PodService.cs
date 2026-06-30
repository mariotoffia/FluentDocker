using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers.Podman;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using Microsoft.Extensions.Logging;

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
    private readonly ILogger<PodService> _logger;
    private readonly string _driverId;
    private readonly string _podName;
    private readonly string _podId;
    private readonly bool _removeOnDispose;
    private readonly Dictionary<string, (ServiceRunningState State, Func<IServiceAsync, Task> Hook)> _hooks = [];
    private ServiceRunningState _state = ServiceRunningState.Stopped;

    public PodService(
        FluentDockerKernel kernel, string driverId,
        string podId, string podName, bool removeOnDispose = false)
    {
      ArgumentNullException.ThrowIfNull(kernel);
      ArgumentNullException.ThrowIfNull(driverId);
      ArgumentNullException.ThrowIfNull(podId);
      _kernel = kernel;
      _logger = kernel.LoggerFactory.CreateLogger<PodService>();
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

      UpdateState(ServiceRunningState.Starting);
      await ExecuteHooksAsync(ServiceRunningState.Starting).ConfigureAwait(false);

      var response = await driver.StartPodAsync(context, _podName, cancellationToken).ConfigureAwait(false);
      if (!response.Success)
      {
        throw new DriverException(
            $"Failed to start pod '{_podName}': {response.Error}",
            ErrorCodes.Pod.StartFailed,
            response.ErrorContext);
      }

      UpdateState(ServiceRunningState.Running);
      await ExecuteHooksAsync(ServiceRunningState.Running).ConfigureAwait(false);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
      var driver = _kernel.SysCtl<IPodmanPodDriver>(_driverId);
      var context = new DriverContext(_driverId);

      UpdateState(ServiceRunningState.Stopping);
      await ExecuteHooksAsync(ServiceRunningState.Stopping).ConfigureAwait(false);

      var response = await driver.StopPodAsync(context, _podName, 10, cancellationToken).ConfigureAwait(false);
      if (!response.Success)
      {
        throw new DriverException(
            $"Failed to stop pod '{_podName}': {response.Error}",
            ErrorCodes.Pod.StopFailed,
            response.ErrorContext);
      }

      UpdateState(ServiceRunningState.Stopped);
      await ExecuteHooksAsync(ServiceRunningState.Stopped).ConfigureAwait(false);
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

      UpdateState(ServiceRunningState.Removing);
      await ExecuteHooksAsync(ServiceRunningState.Removing).ConfigureAwait(false);

      var response = await driver.RemovePodAsync(
          context, _podName, force, cancellationToken).ConfigureAwait(false);
      if (!response.Success)
      {
        throw new DriverException(
            $"Failed to remove pod '{_podName}': {response.Error}",
            ErrorCodes.Pod.RemoveFailed,
            response.ErrorContext);
      }

      UpdateState(ServiceRunningState.Removed);
      await ExecuteHooksAsync(ServiceRunningState.Removed).ConfigureAwait(false);
    }

    public IServiceAsync AddHook(
        ServiceRunningState state, Func<IServiceAsync, Task> hook, string uniqueName = null)
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
      if (!_removeOnDispose)
        return;

      try
      { await RemoveAsync(force: true).ConfigureAwait(false); }
      catch (Exception ex) { _logger.LogWarning(ex, "PodService DisposeAsync failed"); }
    }

    private void UpdateState(ServiceRunningState newState)
    {
      _state = newState;
      StateChange?.Invoke(this, new StateChangeEventArgs(this, newState));
    }

    private async Task ExecuteHooksAsync(ServiceRunningState state)
    {
      foreach (var entry in _hooks.Values)
      {
        if (entry.State != state)
          continue;

        try
        {
          await entry.Hook(this).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
          _logger.LogError(ex, "PodService hook execution failed");
        }
      }
    }
  }
}
