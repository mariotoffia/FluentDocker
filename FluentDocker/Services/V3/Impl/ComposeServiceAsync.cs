using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Services.V3.Impl
{
    /// <summary>
    /// v3.0.0 compose service implementation using kernel and driver.
    /// </summary>
    public class ComposeServiceAsync : IComposeServiceAsync
    {
        private readonly FluentDockerKernel _kernel;
        private readonly string _driverId;
        private readonly List<string> _composeFiles;
        private readonly string _projectName;
        private readonly Dictionary<string, Func<IServiceAsync, Task>> _hooks = new Dictionary<string, Func<IServiceAsync, Task>>();
        private ServiceRunningState _state = ServiceRunningState.Running;

        public ComposeServiceAsync(
            FluentDockerKernel kernel,
            string driverId,
            List<string> composeFiles,
            string projectName)
        {
            _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
            _driverId = driverId ?? throw new ArgumentNullException(nameof(driverId));
            _composeFiles = composeFiles ?? throw new ArgumentNullException(nameof(composeFiles));
            _projectName = projectName ?? throw new ArgumentNullException(nameof(projectName));
        }

        public string Name => _projectName;
        public ServiceRunningState State => _state;
        public FluentDockerKernel Kernel => _kernel;
        public string DriverId => _driverId;
        public string ProjectName => _projectName;
        public IReadOnlyList<string> ComposeFiles => _composeFiles;

        public event ServiceDelegates.StateChange StateChange;

        public async Task<IList<ComposeService>> ListServicesAsync(CancellationToken cancellationToken = default)
        {
            var driver = _kernel.SysCtl<IComposeDriver>(_driverId);
            var context = new DriverContext(_driverId);

            // Use the first compose file for listing
            var composeFile = _composeFiles.Count > 0 ? _composeFiles[0] : throw new InvalidOperationException("No compose file specified");
            var response = await driver.ListAsync(context, composeFile, _projectName, cancellationToken);

            if (!response.Success)
            {
                throw new DriverException(
                    $"Failed to list compose services for project '{_projectName}': {response.Error}",
                    response.ErrorCode,
                    response.ErrorContext);
            }

            return response.Data;
        }

        public async Task<string> GetLogsAsync(bool follow = false, CancellationToken cancellationToken = default)
        {
            var driver = _kernel.SysCtl<IComposeDriver>(_driverId);
            var context = new DriverContext(_driverId);

            var composeFile = _composeFiles.Count > 0 ? _composeFiles[0] : throw new InvalidOperationException("No compose file specified");
            var response = await driver.GetLogsAsync(context, composeFile, follow, cancellationToken);

            if (!response.Success)
            {
                throw new DriverException(
                    $"Failed to get logs for compose project '{_projectName}': {response.Error}",
                    response.ErrorCode,
                    response.ErrorContext);
            }

            return response.Data;
        }

        public async Task<string> ExecuteAsync(string service, string[] command, CancellationToken cancellationToken = default)
        {
            var driver = _kernel.SysCtl<IComposeDriver>(_driverId);
            var context = new DriverContext(_driverId);

            var composeFile = _composeFiles.Count > 0 ? _composeFiles[0] : throw new InvalidOperationException("No compose file specified");
            var response = await driver.ExecuteAsync(context, composeFile, service, command, cancellationToken);

            if (!response.Success)
            {
                throw new DriverException(
                    $"Failed to execute command in service '{service}' for project '{_projectName}': {response.Error}",
                    response.ErrorCode,
                    response.ErrorContext);
            }

            return response.Data;
        }

        public Task ScaleAsync(string service, int replicas, CancellationToken cancellationToken = default)
        {
            // Scale would need to be added to IComposeDriver
            throw new NotImplementedException("ScaleAsync requires IComposeDriver.ScaleAsync");
        }

        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            var driver = _kernel.SysCtl<IComposeDriver>(_driverId);
            var context = new DriverContext(_driverId);

            var composeFile = _composeFiles.Count > 0 ? _composeFiles[0] : throw new InvalidOperationException("No compose file specified");
            var response = await driver.StartAsync(context, composeFile, cancellationToken);

            if (!response.Success)
            {
                throw new DriverException(
                    $"Failed to start compose project '{_projectName}': {response.Error}",
                    response.ErrorCode,
                    response.ErrorContext);
            }

            UpdateState(ServiceRunningState.Running);
            await ExecuteHooksAsync(ServiceRunningState.Running);
        }

        public Task PauseAsync(CancellationToken cancellationToken = default)
        {
            // Compose projects cannot be paused
            throw new NotSupportedException("Compose projects cannot be paused");
        }

        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            var driver = _kernel.SysCtl<IComposeDriver>(_driverId);
            var context = new DriverContext(_driverId);

            var composeFile = _composeFiles.Count > 0 ? _composeFiles[0] : throw new InvalidOperationException("No compose file specified");
            var response = await driver.StopAsync(context, composeFile, timeout: null, cancellationToken);

            if (!response.Success)
            {
                throw new DriverException(
                    $"Failed to stop compose project '{_projectName}': {response.Error}",
                    response.ErrorCode,
                    response.ErrorContext);
            }

            UpdateState(ServiceRunningState.Stopped);
            await ExecuteHooksAsync(ServiceRunningState.Stopped);
        }

        public async Task RemoveAsync(bool force = false, CancellationToken cancellationToken = default)
        {
            var driver = _kernel.SysCtl<IComposeDriver>(_driverId);
            var context = new DriverContext(_driverId);

            var config = new ComposeDownConfig
            {
                ComposeFiles = _composeFiles,
                ProjectName = _projectName,
                RemoveVolumes = force
            };

            var response = await driver.DownAsync(context, config, cancellationToken);

            if (!response.Success)
            {
                throw new DriverException(
                    $"Failed to remove compose project '{_projectName}': {response.Error}",
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

