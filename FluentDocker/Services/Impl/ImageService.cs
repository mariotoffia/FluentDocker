using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Services.Impl
{
    /// <summary>
    /// Image service implementation using kernel and driver.
    /// </summary>
    public class ImageService : IImageService
    {
        private readonly FluentDockerKernel _kernel;
        private readonly string _driverId;
        private readonly string _imageId;
        private readonly string _repository;
        private readonly string _tag;
        private readonly Dictionary<string, Func<IServiceAsync, Task>> _hooks = new Dictionary<string, Func<IServiceAsync, Task>>();
        private ServiceRunningState _state = ServiceRunningState.Running;

        public ImageService(
            FluentDockerKernel kernel,
            string driverId,
            string imageId,
            string repository,
            string tag)
        {
            _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
            _driverId = driverId ?? throw new ArgumentNullException(nameof(driverId));
            _imageId = imageId ?? throw new ArgumentNullException(nameof(imageId));
            _repository = repository;
            _tag = tag ?? "latest";
        }

        public string Name => FullName;
        public ServiceRunningState State => _state;
        public FluentDockerKernel Kernel => _kernel;
        public string DriverId => _driverId;
        public string Id => _imageId;
        public string Tag => _tag;
        public string FullName => string.IsNullOrEmpty(_repository) ? _imageId : $"{_repository}:{_tag}";

        public event ServiceDelegates.StateChange StateChange;

        public async Task<Image> InspectAsync(CancellationToken cancellationToken = default)
        {
            var driver = _kernel.SysCtl<IImageDriver>(_driverId);
            var context = new DriverContext(_driverId);

            var response = await driver.InspectAsync(context, _imageId, cancellationToken);

            if (!response.Success)
            {
                throw new DriverException(
                    $"Failed to inspect image '{FullName}': {response.Error}",
                    response.ErrorCode,
                    response.ErrorContext);
            }

            return response.Data;
        }

        public async Task<IList<ImageLayer>> GetHistoryAsync(CancellationToken cancellationToken = default)
        {
            var driver = _kernel.SysCtl<IImageDriver>(_driverId);
            var context = new DriverContext(_driverId);

            var response = await driver.HistoryAsync(context, _imageId, cancellationToken);

            if (!response.Success)
            {
                throw new DriverException(
                    $"Failed to get history for image '{FullName}': {response.Error}",
                    response.ErrorCode,
                    response.ErrorContext);
            }

            return response.Data;
        }

        public async Task TagAsync(string repository, string tag, CancellationToken cancellationToken = default)
        {
            var driver = _kernel.SysCtl<IImageDriver>(_driverId);
            var context = new DriverContext(_driverId);

            var response = await driver.TagAsync(context, _imageId, repository, tag, cancellationToken);

            if (!response.Success)
            {
                throw new DriverException(
                    $"Failed to tag image '{FullName}' as '{repository}:{tag}': {response.Error}",
                    response.ErrorCode,
                    response.ErrorContext);
            }
        }

        public async Task PushAsync(IProgress<ImagePushProgress> progress = null, CancellationToken cancellationToken = default)
        {
            var driver = _kernel.SysCtl<IImageDriver>(_driverId);
            var context = new DriverContext(_driverId);

            var response = await driver.PushAsync(context, FullName, progress, cancellationToken);

            if (!response.Success)
            {
                throw new DriverException(
                    $"Failed to push image '{FullName}': {response.Error}",
                    response.ErrorCode,
                    response.ErrorContext);
            }
        }

        public async Task SaveAsync(string outputPath, CancellationToken cancellationToken = default)
        {
            var driver = _kernel.SysCtl<IImageDriver>(_driverId);
            var context = new DriverContext(_driverId);

            var response = await driver.SaveAsync(context, new[] { FullName }, outputPath, cancellationToken);

            if (!response.Success)
            {
                throw new DriverException(
                    $"Failed to save image '{FullName}' to '{outputPath}': {response.Error}",
                    response.ErrorCode,
                    response.ErrorContext);
            }
        }

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task PauseAsync(CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Images cannot be paused");
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Images cannot be stopped, use RemoveAsync instead");
        }

        public async Task RemoveAsync(bool force = false, CancellationToken cancellationToken = default)
        {
            var driver = _kernel.SysCtl<IImageDriver>(_driverId);
            var context = new DriverContext(_driverId);

            var response = await driver.RemoveAsync(context, _imageId, force, false, cancellationToken);

            if (!response.Success)
            {
                throw new DriverException(
                    $"Failed to remove image '{FullName}': {response.Error}",
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
            await Task.CompletedTask;
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
                    await hook(this);
                }
                catch
                {
                }
            }
        }
    }
}

