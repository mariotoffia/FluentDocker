using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Drivers;
using Ductus.FluentDocker.Kernel;
using Ductus.FluentDocker.Model.Containers;
using Ductus.FluentDocker.Model.Drivers;

namespace Ductus.FluentDocker.Services.V3.Impl
{
    /// <summary>
    /// v3.0.0 container service implementation using kernel and driver.
    /// </summary>
    public class ContainerServiceAsync : IContainerServiceAsync
    {
        private readonly FluentDockerKernel _kernel;
        private readonly string _driverId;
        private readonly string _containerId;
        private readonly string _image;
        private readonly string _name;
        private readonly Dictionary<string, Func<IServiceAsync, Task>> _hooks = new Dictionary<string, Func<IServiceAsync, Task>>();
        private ServiceRunningState _state = ServiceRunningState.Unknown;

        public ContainerServiceAsync(
            FluentDockerKernel kernel,
            string driverId,
            string containerId,
            string image,
            string name)
        {
            _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
            _driverId = driverId ?? throw new ArgumentNullException(nameof(driverId));
            _containerId = containerId ?? throw new ArgumentNullException(nameof(containerId));
            _image = image;
            _name = name ?? $"container-{containerId}";
        }

        public string Name => _name;
        public ServiceRunningState State => _state;
        public FluentDockerKernel Kernel => _kernel;
        public string DriverId => _driverId;
        public string Id => _containerId;
        public string Image => _image;

        public event ServiceDelegates.StateChange StateChange;

        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            var driver = _kernel.SysCtl<IContainerDriver>(_driverId);
            var context = new DriverContext(_driverId);

            var response = await driver.StartAsync(context, _containerId, cancellationToken);

            if (!response.Success)
            {
                throw new ContainerStartException(
                    $"Failed to start container '{_name}': {response.Error}",
                    response.ErrorCode,
                    response.ErrorContext);
            }

            UpdateState(ServiceRunningState.Running);
            await ExecuteHooksAsync(ServiceRunningState.Running);
        }

        public async Task PauseAsync(CancellationToken cancellationToken = default)
        {
            // Docker CLI doesn't have pause in our driver yet, but structure is here
            UpdateState(ServiceRunningState.Paused);
            await ExecuteHooksAsync(ServiceRunningState.Paused);
        }

        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            var driver = _kernel.SysCtl<IContainerDriver>(_driverId);
            var context = new DriverContext(_driverId);

            var response = await driver.StopAsync(context, _containerId, timeout: null, cancellationToken);

            if (!response.Success)
            {
                throw new DriverException(
                    $"Failed to stop container '{_name}': {response.Error}",
                    response.ErrorCode,
                    response.ErrorContext);
            }

            UpdateState(ServiceRunningState.Stopped);
            await ExecuteHooksAsync(ServiceRunningState.Stopped);
        }

        public async Task RemoveAsync(bool force = false, CancellationToken cancellationToken = default)
        {
            var driver = _kernel.SysCtl<IContainerDriver>(_driverId);
            var context = new DriverContext(_driverId);

            var response = await driver.RemoveAsync(context, _containerId, force, cancellationToken);

            if (!response.Success)
            {
                throw new DriverException(
                    $"Failed to remove container '{_name}': {response.Error}",
                    response.ErrorCode,
                    response.ErrorContext);
            }

            UpdateState(ServiceRunningState.Removed);
            await ExecuteHooksAsync(ServiceRunningState.Removed);
        }

        public async Task<Container> InspectAsync(CancellationToken cancellationToken = default)
        {
            var driver = _kernel.SysCtl<IContainerDriver>(_driverId);
            var context = new DriverContext(_driverId);

            var response = await driver.InspectAsync(context, _containerId, cancellationToken);

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
                _state = ParseState(response.Data.State);
            }

            return response.Data;
        }

        public async Task<string> GetLogsAsync(bool follow = false, CancellationToken cancellationToken = default)
        {
            var driver = _kernel.SysCtl<IContainerDriver>(_driverId);
            var context = new DriverContext(_driverId);

            var response = await driver.GetLogsAsync(context, _containerId, follow, cancellationToken);

            if (!response.Success)
            {
                throw new DriverException(
                    $"Failed to get logs for container '{_name}': {response.Error}",
                    response.ErrorCode);
            }

            return response.Data;
        }

        public Task<string> ExecuteAsync(string command, CancellationToken cancellationToken = default)
        {
            // Would need to add Execute to IContainerDriver
            throw new NotImplementedException("ExecuteAsync requires IContainerDriver.ExecuteAsync");
        }

        public Task<byte[]> ExportAsync(CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException("ExportAsync not yet implemented");
        }

        public Task<byte[]> CopyFromAsync(string containerPath, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException("CopyFromAsync not yet implemented");
        }

        public Task CopyToAsync(string containerPath, byte[] data, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException("CopyToAsync not yet implemented");
        }

        public Task<ContainerStats> GetStatsAsync(CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException("GetStatsAsync not yet implemented");
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

        public void Dispose()
        {
            DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (_state == ServiceRunningState.Running || _state == ServiceRunningState.Paused)
                {
                    await StopAsync();
                }

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
            StateChange?.Invoke(this, new StateChangeEventArgs(oldState, newState, this));
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

        private ServiceRunningState ParseState(string state)
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
    }
}
