using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using Microsoft.Extensions.Logging;

namespace FluentDocker.Services.Impl
{
  /// <inheritdoc />
  /// <remarks>
  /// Lifecycle transitions are individually atomic; a single service instance is not designed
  /// for concurrent lifecycle calls (Start/Stop/Remove/Dispose) from multiple threads.
  /// </remarks>
  public class NetworkService : INetworkService, IServiceCapabilities
  {
    // IServiceCapabilities
    bool IServiceCapabilities.CanStart => false;
    bool IServiceCapabilities.CanStop => false;
    bool IServiceCapabilities.CanPause => false;
    bool IServiceCapabilities.CanRemove => true;
    bool IServiceCapabilities.CanHook => true;

    private readonly FluentDockerKernel _kernel;
    private readonly ILogger<NetworkService> _logger;
    private readonly string _driverId;
    private readonly string _networkId;
    private readonly string _networkName;
    private readonly bool _removeOnDispose;
    private readonly TimeSpan _disposeCleanupTimeout;
    private readonly ConcurrentDictionary<string, (ServiceRunningState State, Func<IServiceAsync, Task> Hook)> _hooks = [];
    private readonly object _stateLock = new();
    private volatile ServiceRunningState _state = ServiceRunningState.Running;

    public NetworkService(
        FluentDockerKernel kernel,
        string driverId,
        string networkId,
        string networkName,
        bool removeOnDispose = false,
        TimeSpan? disposeCleanupTimeout = null)
    {
      ArgumentNullException.ThrowIfNull(kernel);
      ArgumentNullException.ThrowIfNull(driverId);
      ArgumentNullException.ThrowIfNull(networkId);
      _kernel = kernel;
      _logger = kernel.LoggerFactory.CreateLogger<NetworkService>();
      _driverId = driverId;
      _networkId = networkId;
      _networkName = networkName ?? $"network-{networkId}";
      _removeOnDispose = removeOnDispose;
      _disposeCleanupTimeout =
          disposeCleanupTimeout ?? TimeSpan.FromMilliseconds(ContainerService.DefaultDisposeCleanupTimeoutMs);
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
      cancellationToken.ThrowIfCancellationRequested();
      ThrowIfDisposed();
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
      cancellationToken.ThrowIfCancellationRequested();
      ThrowIfDisposed();
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

    /// <summary>
    /// Returns the names of containers connected to this network.
    /// </summary>
    /// <remarks>
    /// Container names come from network inspect membership; when a runtime omits a name,
    /// the container ID key is returned instead. A missing network still surfaces as a
    /// <see cref="DriverException"/> from <see cref="InspectAsync"/>.
    /// </remarks>
    public async Task<IList<string>> GetConnectedContainersAsync(CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      ThrowIfDisposed();
      var network = await InspectAsync(cancellationToken).ConfigureAwait(false);
      var containers = new List<string>();

      if (network.Containers == null)
        return containers;

      foreach (var entry in network.Containers)
        containers.Add(string.IsNullOrEmpty(entry.Value?.Name) ? entry.Key : entry.Value.Name);

      return containers;
    }

    public async Task<Network> InspectAsync(CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      ThrowIfDisposed();
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

    /// <summary>Networks are already active when represented; start is a no-op.</summary>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      ThrowIfDisposed();
      return Task.CompletedTask;
    }

    public Task PauseAsync(CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      ThrowIfDisposed();
      throw new FluentDockerNotSupportedException("Networks cannot be paused");
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      ThrowIfDisposed();
      throw new FluentDockerNotSupportedException("Networks cannot be stopped, use RemoveAsync instead");
    }

    /// <summary>Removes the network.</summary>
    /// <remarks>The <paramref name="force"/> parameter is ignored because network drivers do not support it.</remarks>
    public async Task RemoveAsync(bool force = false, CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      ThrowIfDisposed();
      if (State == ServiceRunningState.Removed)
        return;

      var driver = _kernel.SysCtl<INetworkDriver>(_driverId);
      var context = new DriverContext(_driverId);

      UpdateState(ServiceRunningState.Removing);
      await ExecuteHooksAsync(ServiceRunningState.Removing).ConfigureAwait(false);

      var response = await driver.RemoveAsync(context, _networkId, cancellationToken).ConfigureAwait(false);

      if (!response.Success)
      {
        if (IsNetworkAlreadyGone(response))
        {
          UpdateState(ServiceRunningState.Removed);
          await ExecuteHooksAsync(ServiceRunningState.Removed).ConfigureAwait(false);
          return;
        }

        UpdateState(ServiceRunningState.Unknown);
        throw new DriverException(
            $"Failed to remove network '{_networkName}': {response.Error}",
            response.ErrorCode,
            response.ErrorContext);
      }

      UpdateState(ServiceRunningState.Removed);
      await ExecuteHooksAsync(ServiceRunningState.Removed).ConfigureAwait(false);
    }

    // Docker CLI reports a missing network as "<id> not found" with a generic RemoveFailed code
    // (only Podman/the API driver set the typed NotFound). The "not found" substring is therefore
    // required for CLI remove idempotency, but must be anchored to the network id — otherwise an
    // unrelated "network driver plugin xyz not found" would be mis-read as already-gone.
    private bool IsNetworkAlreadyGone(CommandResponse<Unit> response) =>
        response.ErrorCode == ErrorCodes.Network.NotFound ||
        response.Error?.Contains("no such network", StringComparison.OrdinalIgnoreCase) == true ||
        (response.Error?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true &&
         response.Error.Contains(_networkId, StringComparison.OrdinalIgnoreCase));

    public IServiceAsync AddHook(ServiceRunningState state, Func<IServiceAsync, Task> hook, string uniqueName = null)
    {
      ThrowIfDisposed();
      var name = uniqueName ?? Guid.NewGuid().ToString();
      _hooks[name] = (state, hook);
      return this;
    }

    public IServiceAsync RemoveHook(string uniqueName)
    {
      ThrowIfDisposed();
      _hooks.TryRemove(uniqueName, out _);
      return this;
    }

    private int _disposed;
    private int _disposeCompleted;

    public void Dispose()
    {
      if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        return;
      try
      {
        // Dispatched to the thread pool to avoid sync-over-async deadlocks.
        Task.Run(() => DisposeCoreAsync().AsTask()).GetAwaiter().GetResult();
      }
      finally
      {
        Volatile.Write(ref _disposeCompleted, 1);
        GC.SuppressFinalize(this);
      }
    }

    public async ValueTask DisposeAsync()
    {
      if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        return;
      try
      {
        await DisposeCoreAsync().ConfigureAwait(false);
      }
      finally
      {
        Volatile.Write(ref _disposeCompleted, 1);
        GC.SuppressFinalize(this);
      }
    }

    private async ValueTask DisposeCoreAsync()
    {
      if (!_removeOnDispose)
        return;

      using var cleanupCts = new CancellationTokenSource(_disposeCleanupTimeout);
      var removeTask = RemoveAsync(force: true, cleanupCts.Token);
      try
      {
        await removeTask.WaitAsync(cleanupCts.Token).ConfigureAwait(false);
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "NetworkService DisposeAsync failed");
        ObserveAbandonedCleanup(removeTask);
      }
    }

    private static void ObserveAbandonedCleanup(Task task) =>
        _ = task.ContinueWith(
            static t => _ = t.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeCompleted) != 0, this);

    private void UpdateState(ServiceRunningState newState)
    {
      ServiceDelegates.StateChange stateChange;
      StateChangeEventArgs args;
      lock (_stateLock)
      {
        if (Volatile.Read(ref _disposeCompleted) != 0 || _state == newState)
          return;

        _state = newState;
        stateChange = StateChange;
        if (stateChange == null)
          return;

        args = new StateChangeEventArgs(this, newState);
      }

      StateChangeNotifier.Invoke(stateChange, args, _logger, "NetworkService");
    }

    private async Task ExecuteHooksAsync(ServiceRunningState state)
    {
      if (Volatile.Read(ref _disposeCompleted) != 0)
        return;

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
          _logger.LogError(ex, "NetworkService hook execution failed");
        }
      }
    }
  }
}
