using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Services.V3.Impl
{
    /// <summary>
    /// v3.0.0 network service implementation using kernel and driver.
    /// </summary>
    public class NetworkServiceAsync : INetworkServiceAsync
    {
        private readonly FluentDockerKernel _kernel;
        private readonly string _driverId;
        private readonly string _networkId;
        private readonly string _networkName;
        private readonly Dictionary<string, Func<IServiceAsync, Task>> _hooks = new Dictionary<string, Func<IServiceAsync, Task>>();
        private ServiceRunningState _state = ServiceRunningState.Running;

        public NetworkServiceAsync(
            FluentDockerKernel kernel,
            string driverId,
            string networkId,
            string networkName)
        {
            _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
            _driverId = driverId ?? throw new ArgumentNullException(nameof(driverId));
            _networkId = networkId ?? throw new ArgumentNullException(nameof(networkId));
            _networkName = networkName ?? $"network-{networkId}";
        }

        public string Name => _networkName;
        public ServiceRunningState State => _state;
        public FluentDockerKernel Kernel => _kernel;
        public string DriverId => _driverId;
        public string Id => _networkId;
        public string NetworkName => _networkName;

        public event ServiceDelegates.StateChange StateChange;

        public async Task ConnectAsync(string containerId, CancellationToken cancellationToken = default)
        {
            var driver = _kernel.SysCtl<INetworkDriver>(_driverId);
            var context = new DriverContext(_driverId);

            var response = await driver.ConnectAsync(context, _networkId, containerId, cancellationToken);

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

            var response = await driver.DisconnectAsync(context, _networkId, containerId, force, cancellationToken);

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
            var network = await InspectAsync(cancellationToken);
            // Network.Containers would need to be added to the Network model
            return new List<string>();
        }

        public async Task<Network> InspectAsync(CancellationToken cancellationToken = default)
        {
            var driver = _kernel.SysCtl<INetworkDriver>(_driverId);
            var context = new DriverContext(_driverId);

            var response = await driver.InspectAsync(context, _networkId, cancellationToken);

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
            // Networks are always "running" once created
            return Task.CompletedTask;
        }

        public Task PauseAsync(CancellationToken cancellationToken = default)
        {
            // Networks cannot be paused
            throw new NotSupportedException("Networks cannot be paused");
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            // Networks cannot be stopped, only removed
            throw new NotSupportedException("Networks cannot be stopped, use RemoveAsync instead");
        }

        public async Task RemoveAsync(bool force = false, CancellationToken cancellationToken = default)
        {
            var driver = _kernel.SysCtl<INetworkDriver>(_driverId);
            var context = new DriverContext(_driverId);

            var response = await driver.RemoveAsync(context, _networkId, cancellationToken);

            if (!response.Success)
            {
                throw new DriverException(
                    $"Failed to remove network '{_networkName}': {response.Error}",
                    response.ErrorCode,
                    response.ErrorContext);
            }

            UpdateState(ServiceRunningState.Removed);
            await ExecuteHooksAsync(ServiceRunningState.Removed);
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

        // IService synchronous method implementations
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

        public void Dispose()
        {
#if NETSTANDARD2_0
            DisposeAsync().GetAwaiter().GetResult();
#else
            DisposeAsync().AsTask().GetAwaiter().GetResult();
#endif
        }

#if NETSTANDARD2_0
        public async Task DisposeAsync()
#else
        public async ValueTask DisposeAsync()
#endif
        {
            try
            {
                await RemoveAsync(force: true);
            }
            catch
            {
                // Ignore errors during disposal
            }
        }

        private void UpdateState(ServiceRunningState newState)
        {
            var oldState = _state;
            _state = newState;
            StateChange?.Invoke(this, new StateChangeEventArgs(this, newState));
        }

        private async Task ExecuteHooksAsync(ServiceRunningState state)
        {
            foreach (var hook in _hooks.Values)
            {
                try
                {
                    await hook(this);
                }
                catch
                {
                    // Ignore hook errors
                }
            }
        }
    }
}

