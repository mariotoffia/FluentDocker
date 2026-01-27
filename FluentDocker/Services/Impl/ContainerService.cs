using System;
using System.Collections.Generic;
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

namespace FluentDocker.Services.Impl
{
    /// <summary>
    /// Container service implementation using kernel and driver.
    /// </summary>
    public class ContainerService : IContainerService
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
            _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
            _driverId = driverId ?? throw new ArgumentNullException(nameof(driverId));
            _containerId = containerId ?? throw new ArgumentNullException(nameof(containerId));
            _image = image;
            _name = name ?? $"container-{containerId}";
            _stopOnDispose = stopOnDispose;
            _deleteOnDispose = deleteOnDispose;
            _deleteVolumeOnDispose = deleteVolumeOnDispose;
            _deleteNamedVolumeOnDispose = deleteNamedVolumeOnDispose;
            _customResolver = customResolver;
            _lifecycleHooks = lifecycleHooks ?? new List<LifecycleHook>();
            
            // Initialize state hook lists
            foreach (ServiceRunningState state in Enum.GetValues(typeof(ServiceRunningState)))
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

        public event ServiceDelegates.StateChange StateChange;

        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            var driver = _kernel.SysCtl<IContainerDriver>(_driverId);
            var context = new DriverContext(_driverId);

            UpdateState(ServiceRunningState.Starting);
            await ExecuteHooksAsync(ServiceRunningState.Starting);

            var response = await driver.StartAsync(context, _containerId, cancellationToken);

            if (!response.Success)
            {
                throw new ContainerStartException(
                    _containerId,
                    response.Error,
                    response.ErrorContext);
            }

            UpdateState(ServiceRunningState.Running);
            await ExecuteHooksAsync(ServiceRunningState.Running);
            await ExecuteLifecycleHooksAsync(ServiceRunningState.Running, cancellationToken);
        }

        public async Task PauseAsync(CancellationToken cancellationToken = default)
        {
            var driver = _kernel.SysCtl<IContainerDriver>(_driverId);
            var context = new DriverContext(_driverId);

            var response = await driver.PauseAsync(context, _containerId, cancellationToken);

            if (!response.Success)
            {
                throw new DriverException(
                    $"Failed to pause container '{_name}': {response.Error}",
                    response.ErrorCode,
                    response.ErrorContext);
            }

            UpdateState(ServiceRunningState.Paused);
            await ExecuteHooksAsync(ServiceRunningState.Paused);
        }

        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            var driver = _kernel.SysCtl<IContainerDriver>(_driverId);
            var context = new DriverContext(_driverId);

            UpdateState(ServiceRunningState.Stopping);
            await ExecuteHooksAsync(ServiceRunningState.Stopping);

            var response = await driver.StopAsync(context, _containerId, null, cancellationToken);

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

            UpdateState(ServiceRunningState.Removing);
            await ExecuteHooksAsync(ServiceRunningState.Removing);
            await ExecuteLifecycleHooksAsync(ServiceRunningState.Removing, cancellationToken);

            var removeVolumes = _deleteVolumeOnDispose || _deleteNamedVolumeOnDispose;
            var response = await driver.RemoveAsync(context, _containerId, force, removeVolumes, cancellationToken);

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
                _state = ParseState(response.Data.State.Status);
            }

            return response.Data;
        }

        public async Task<string> GetLogsAsync(bool follow = false, CancellationToken cancellationToken = default)
        {
            var driver = _kernel.SysCtl<IContainerDriver>(_driverId);
            var context = new DriverContext(_driverId);

            var response = await driver.GetLogsAsync(context, _containerId, follow, null, false, cancellationToken);

            if (!response.Success)
            {
                throw new DriverException(
                    $"Failed to get logs for container '{_name}': {response.Error}",
                    response.ErrorCode);
            }

            return response.Data;
        }

        public async Task<string> ExecuteAsync(string command, CancellationToken cancellationToken = default)
        {
            var driver = _kernel.SysCtl<IContainerDriver>(_driverId);
            var context = new DriverContext(_driverId);

            var config = new ExecConfig
            {
                Command = command.Split(' ')
            };

            var response = await driver.ExecAsync(context, _containerId, config, cancellationToken);

            if (!response.Success)
            {
                throw new DriverException(
                    $"Failed to execute command in container '{_name}': {response.Error}",
                    response.ErrorCode);
            }

            return response.Data?.StdOut;
        }

        public async Task<byte[]> ExportAsync(CancellationToken cancellationToken = default)
        {
            var driver = _kernel.SysCtl<IContainerDriver>(_driverId);
            var context = new DriverContext(_driverId);

            // Create a temp file for export
            var tempPath = Path.GetTempFileName();
            try
            {
                var response = await driver.ExportAsync(context, _containerId, tempPath, cancellationToken);

                if (!response.Success)
                {
                    throw new DriverException(
                        $"Failed to export container '{_name}': {response.Error}",
                        response.ErrorCode);
                }

                return await File.ReadAllBytesAsync(tempPath, cancellationToken);
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        }

        public async Task<byte[]> CopyFromAsync(string containerPath, CancellationToken cancellationToken = default)
        {
            var driver = _kernel.SysCtl<IContainerDriver>(_driverId);
            var context = new DriverContext(_driverId);

            // Create a temp file for copy
            var tempPath = Path.GetTempFileName();
            try
            {
                var response = await driver.CopyFromAsync(context, _containerId, containerPath, tempPath, cancellationToken);

                if (!response.Success)
                {
                    throw new DriverException(
                        $"Failed to copy from container '{_name}': {response.Error}",
                        response.ErrorCode);
                }

                return await File.ReadAllBytesAsync(tempPath, cancellationToken);
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        }

        public async Task CopyToAsync(string containerPath, byte[] data, CancellationToken cancellationToken = default)
        {
            var driver = _kernel.SysCtl<IContainerDriver>(_driverId);
            var context = new DriverContext(_driverId);

            // Write data to temp file
            var tempPath = Path.GetTempFileName();
            try
            {
                await File.WriteAllBytesAsync(tempPath, data, cancellationToken);

                var response = await driver.CopyToAsync(context, _containerId, tempPath, containerPath, cancellationToken);

                if (!response.Success)
                {
                    throw new DriverException(
                        $"Failed to copy to container '{_name}': {response.Error}",
                        response.ErrorCode);
                }
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        }

        /// <summary>
        /// Copies a file or directory from the host to the container.
        /// </summary>
        /// <param name="hostPath">Source path on the host (file or directory).</param>
        /// <param name="containerPath">Destination path in the container.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async Task CopyToAsync(string hostPath, string containerPath, CancellationToken cancellationToken = default)
        {
            if (!File.Exists(hostPath) && !Directory.Exists(hostPath))
            {
                throw new FileNotFoundException($"Source path does not exist: {hostPath}");
            }

            var driver = _kernel.SysCtl<IContainerDriver>(_driverId);
            var context = new DriverContext(_driverId);

            var response = await driver.CopyToAsync(context, _containerId, hostPath, containerPath, cancellationToken);

            if (!response.Success)
            {
                throw new DriverException(
                    $"Failed to copy to container '{_name}': {response.Error}",
                    response.ErrorCode);
            }
        }

        /// <summary>
        /// Copies a file or directory from the container to the host.
        /// </summary>
        /// <param name="containerPath">Source path in the container.</param>
        /// <param name="hostPath">Destination path on the host.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async Task CopyFromToPathAsync(string containerPath, string hostPath, CancellationToken cancellationToken = default)
        {
            var driver = _kernel.SysCtl<IContainerDriver>(_driverId);
            var context = new DriverContext(_driverId);

            // Ensure the destination directory exists
            var dir = Path.GetDirectoryName(hostPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var response = await driver.CopyFromAsync(context, _containerId, containerPath, hostPath, cancellationToken);

            if (!response.Success)
            {
                throw new DriverException(
                    $"Failed to copy from container '{_name}': {response.Error}",
                    response.ErrorCode);
            }
        }

        public async Task<ContainerStats> GetStatsAsync(CancellationToken cancellationToken = default)
        {
            var driver = _kernel.SysCtl<IContainerDriver>(_driverId);
            var context = new DriverContext(_driverId);
            var response = await driver.StatsAsync(context, _containerId, cancellationToken);

            if (!response.Success)
            {
                throw new DriverException(
                    $"Failed to get stats for container '{_name}': {response.Error}",
                    response.ErrorCode);
            }

            var driverStats = response.Data;
            return new ContainerStats
            {
                ContainerId = driverStats.ContainerId,
                Cpu = new CpuStats
                {
                    UsagePercent = driverStats.CpuPercent,
                    SystemCpuUsage = 0, // Not available from docker stats command
                    ContainerCpuUsage = 0 // Not available from docker stats command
                },
                Memory = new MemoryStats
                {
                    Usage = driverStats.MemoryUsage,
                    Limit = driverStats.MemoryLimit,
                    UsagePercent = driverStats.MemoryPercent
                },
                Network = new NetworkStats
                {
                    RxBytes = driverStats.NetworkRxBytes,
                    TxBytes = driverStats.NetworkTxBytes,
                    RxPackets = 0, // Not available from docker stats command
                    TxPackets = 0 // Not available from docker stats command
                },
                Disk = new DiskStats
                {
                    ReadBytes = driverStats.BlockReadBytes,
                    WriteBytes = driverStats.BlockWriteBytes
                }
            };
        }

        /// <summary>
        /// Gets the host port for a container port, using custom resolver if configured.
        /// </summary>
        public async Task<int> GetHostPortAsync(string portAndProto, CancellationToken cancellationToken = default)
        {
            var config = await InspectAsync(cancellationToken);
            
            if (_customResolver != null && config?.NetworkSettings?.Ports != null)
            {
                var endpoint = _customResolver(config.NetworkSettings.Ports, portAndProto, null);
                return endpoint?.Port ?? 0;
            }

            if (config?.NetworkSettings?.Ports == null)
                return 0;

            if (!config.NetworkSettings.Ports.TryGetValue(portAndProto, out var bindings) || 
                bindings == null || bindings.Length == 0)
                return 0;

            var binding = bindings[0];
            return int.TryParse(binding.HostPort, out var port) ? port : 0;
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
                if (_stopOnDispose && 
                    (_state == ServiceRunningState.Running || _state == ServiceRunningState.Paused))
                {
                    await StopAsync();
                }

                if (_deleteOnDispose)
                {
                    await RemoveAsync(force: true);
                }
            }
            catch
            {
                // Ignore errors during disposal
            }
        }

        #endregion

        #region Private Methods

        private void UpdateState(ServiceRunningState newState)
        {
            var oldState = _state;
            _state = newState;
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
                    await hook(this);
                }
                catch
                {
                    // Ignore hook errors
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
                                await CopyToAsync(hook.HostPath, hook.ContainerPath, cancellationToken);
                            }
                            break;

                        case LifecycleHookType.CopyFrom:
                            // Use path-based copy which supports both files and directories
                            await CopyFromToPathAsync(hook.ContainerPath, hook.HostPath, cancellationToken);
                            break;

                        case LifecycleHookType.Export:
                            if (hook.Condition == null || hook.Condition(this))
                            {
                                var exportData = await ExportAsync(cancellationToken);
                                var exportDir = Path.GetDirectoryName(hook.HostPath);
                                if (!string.IsNullOrEmpty(exportDir) && !Directory.Exists(exportDir))
                                    Directory.CreateDirectory(exportDir);

                                if (hook.Explode)
                                {
                                    // Extract tar to directory
                                    // Simplified - would need proper tar extraction
                                    await File.WriteAllBytesAsync(hook.HostPath + ".tar", exportData, cancellationToken);
                                }
                                else
                                {
                                    await File.WriteAllBytesAsync(hook.HostPath, exportData, cancellationToken);
                                }
                            }
                            break;

                        case LifecycleHookType.Execute:
                            if (hook.Command?.Length > 0)
                            {
                                await ExecuteAsync(string.Join(" ", hook.Command), cancellationToken);
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

        #endregion
    }
}
