using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;

#pragma warning disable CS0618 // IService obsolete — intentional usage

namespace FluentDocker.Services.Impl
{
  /// <summary>
  /// Network service implementation using kernel and driver.
  /// </summary>
  public class NetworkService : INetworkService, IServiceCapabilities
  {
    // IServiceCapabilities
    bool IServiceCapabilities.CanStart => false;
    bool IServiceCapabilities.CanStop => false;
    bool IServiceCapabilities.CanPause => false;
    bool IServiceCapabilities.CanRemove => true;

    private readonly FluentDockerKernel _kernel;
    private readonly string _driverId;
    private readonly string _networkId;
    private readonly string _networkName;
    private readonly bool _removeOnDispose;
    private readonly Dictionary<string, Func<IServiceAsync, Task>> _hooks = new Dictionary<string, Func<IServiceAsync, Task>>();
    private ServiceRunningState _state = ServiceRunningState.Running;

    public NetworkService(
        FluentDockerKernel kernel,
        string driverId,
        string networkId,
        string networkName,
        bool removeOnDispose = false)
    {
      ArgumentNullException.ThrowIfNull(kernel);
      ArgumentNullException.ThrowIfNull(driverId);
      ArgumentNullException.ThrowIfNull(networkId);
      _kernel = kernel;
      _driverId = driverId;
      _networkId = networkId;
      _networkName = networkName ?? $"network-{networkId}";
      _removeOnDispose = removeOnDispose;
    }

    public string Name => _networkName;
    public ServiceRunningState State => _state;
    public FluentDockerKernel Kernel => _kernel;
    public string DriverId => _driverId;
    public string Id => _networkId;
    public string NetworkName => _networkName;

#pragma warning disable CA1710 // Delegate name 'StateChange' — intentional API design
    public event ServiceDelegates.StateChange StateChange;
#pragma warning restore CA1710

    public async Task ConnectAsync(string containerId, CancellationToken cancellationToken = default)
    {
      var driver = _kernel.SysCtl<INetworkDriver>(_driverId);
      var context = new DriverContext(_driverId);

      var response = await driver.ConnectAsync(context, _networkId, containerId, cancellationToken).ConfigureAwait(false);

      if (!response.Success)
      {
        throw new DriverException(
            $"Failed to connect container '{containerId}' to network '{_networkName}': {response.Error}",
            response.ErrorCode,
            response.ErrorContext);
      }
    }

    public async Task DisconnectAsync(string containerId, bool force = false, CancellationToken cancellationToken = default)
    {
      var driver = _kernel.SysCtl<INetworkDriver>(_driverId);
      var context = new DriverContext(_driverId);

      var response = await driver.DisconnectAsync(context, _networkId, containerId, force, cancellationToken).ConfigureAwait(false);

      if (!response.Success)
      {
        throw new DriverException(
            $"Failed to disconnect container '{containerId}' from network '{_networkName}': {response.Error}",
            response.ErrorCode,
            response.ErrorContext);
      }
    }

    public async Task<IList<string>> GetConnectedContainersAsync(CancellationToken cancellationToken = default)
    {
      await InspectAsync(cancellationToken).ConfigureAwait(false);
      return new List<string>();
    }

    public async Task<Network> InspectAsync(CancellationToken cancellationToken = default)
    {
      var driver = _kernel.SysCtl<INetworkDriver>(_driverId);
      var context = new DriverContext(_driverId);

      var response = await driver.InspectAsync(context, _networkId, cancellationToken).ConfigureAwait(false);

      if (!response.Success)
      {
        throw new DriverException(
            $"Failed to inspect network '{_networkName}': {response.Error}",
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
      throw new NotSupportedException("Networks cannot be paused");
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
      throw new NotSupportedException("Networks cannot be stopped, use RemoveAsync instead");
    }

    public async Task RemoveAsync(bool force = false, CancellationToken cancellationToken = default)
    {
      var driver = _kernel.SysCtl<INetworkDriver>(_driverId);
      var context = new DriverContext(_driverId);

      var response = await driver.RemoveAsync(context, _networkId, cancellationToken).ConfigureAwait(false);

      if (!response.Success)
      {
        throw new DriverException(
            $"Failed to remove network '{_networkName}': {response.Error}",
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
        Logger.Log($"NetworkService DisposeAsync failed: {ex.Message}");
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
          Logger.Log($"NetworkService hook execution failed: {ex.Message}");
        }
      }
    }
  }
}

