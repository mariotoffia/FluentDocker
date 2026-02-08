using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Kernel;
using FluentDocker.Services;

namespace FluentDocker.Builders
{
    /// <summary>
    /// v3.0.0 async builder with WithinDriver() scoping and terminal BuildAsync().
    /// </summary>
    public class Builder : IBuilder
    {
        private FluentDockerKernel _currentKernel;
        private string _currentDriverId;
        private readonly List<BuildOperation> _operations = new List<BuildOperation>();

        /// <summary>
        /// Creates a new builder.
        /// </summary>
        public Builder()
        {
        }

        /// <summary>
        /// Establishes a driver scope for subsequent operations.
        /// </summary>
        /// <param name="driverId">Driver identifier</param>
        /// <param name="kernel">Kernel instance (reuses previous if null)</param>
        /// <returns>This builder for fluent chaining</returns>
        public Builder WithinDriver(string driverId, FluentDockerKernel kernel = null)
        {
            _currentKernel = kernel ?? _currentKernel;

            if (_currentKernel == null)
            {
                throw new InvalidOperationException(
                    "Kernel required in first WithinDriver() call. " +
                    "Provide a kernel or create one with FluentDockerKernel.Create().BuildAsync()");
            }

            _currentDriverId = driverId;
            return this;
        }

        /// <summary>
        /// Adds a container operation to the current scope.
        /// </summary>
        public Builder UseContainer(Action<IContainerBuilder> configure)
        {
            ValidateScope();
            var builder = new ContainerBuilder(_currentKernel, _currentDriverId);
            configure(builder);
            _operations.Add(new BuildOperation
            {
                Kernel = _currentKernel, DriverId = _currentDriverId,
                ExecuteAsync = ct => builder.ExecuteAsync(ct)
            });
            return this;
        }

        /// <summary>
        /// Adds a network operation to the current scope.
        /// </summary>
        public Builder UseNetwork(Action<INetworkBuilder> configure)
        {
            ValidateScope();
            var builder = new NetworkBuilder(_currentKernel, _currentDriverId);
            configure(builder);
            _operations.Add(new BuildOperation
            {
                Kernel = _currentKernel, DriverId = _currentDriverId,
                ExecuteAsync = ct => builder.ExecuteAsync(ct)
            });
            return this;
        }

        /// <summary>
        /// Adds a volume operation to the current scope.
        /// </summary>
        public Builder UseVolume(Action<IVolumeBuilder> configure)
        {
            ValidateScope();
            var builder = new VolumeBuilder(_currentKernel, _currentDriverId);
            configure(builder);
            _operations.Add(new BuildOperation
            {
                Kernel = _currentKernel, DriverId = _currentDriverId,
                ExecuteAsync = ct => builder.ExecuteAsync(ct)
            });
            return this;
        }

        /// <summary>
        /// Adds a compose operation to the current scope.
        /// </summary>
        public Builder UseCompose(Action<IComposeBuilder> configure)
        {
            ValidateScope();
            var builder = new ComposeBuilder(_currentKernel, _currentDriverId);
            configure(builder);
            _operations.Add(new BuildOperation
            {
                Kernel = _currentKernel, DriverId = _currentDriverId,
                ExecuteAsync = ct => builder.ExecuteAsync(ct)
            });
            return this;
        }

        /// <summary>
        /// Adds an image build operation to the current scope.
        /// </summary>
        public Builder UseImage(string imageName, Action<DockerfileBuilder> configure)
        {
            ValidateScope();
            var imageBuilder = new ImageBuilder(_currentKernel, _currentDriverId, imageName);
            var dockerfileBuilder = imageBuilder.From();
            configure(dockerfileBuilder);
            _operations.Add(new BuildOperation
            {
                Kernel = _currentKernel, DriverId = _currentDriverId,
                ExecuteAsync = ct => imageBuilder.ExecuteAsync(ct).ContinueWith(t => (IService)t.Result, ct)
            });
            return this;
        }

        /// <summary>
        /// TERMINAL - Builds all operations synchronously.
        /// </summary>
        public BuildResults Build()
        {
            return Task.Run(() => BuildAsync()).GetAwaiter().GetResult();
        }

        /// <summary>
        /// TERMINAL - Builds all operations asynchronously.
        /// </summary>
        public async Task<BuildResults> BuildAsync(CancellationToken cancellationToken = default)
        {
            var scopes = new Dictionary<(FluentDockerKernel, string), BuildScope>();
            var groupedOps = _operations.GroupBy(op => (op.Kernel, op.DriverId));

            foreach (var group in groupedOps)
            {
                var key = group.Key;
                var scope = new BuildScope(key.Kernel, key.DriverId);
                scopes[key] = scope;

                foreach (var operation in group)
                {
                    var service = await operation.ExecuteAsync(cancellationToken);
                    scope.AddResult(service);
                }

                await StartContainersWithLinksAsync(scope, cancellationToken);
            }

            return new BuildResults(scopes.Values.ToList());
        }

        private async Task StartContainersWithLinksAsync(BuildScope scope, CancellationToken cancellationToken)
        {
            var containersToStart = scope.Results
                .OfType<IContainerService>()
                .Where(c => c.State != ServiceRunningState.Running)
                .ToList();

            if (containersToStart.Count == 0)
                return;

            var driver = scope.Kernel.SysCtl<Drivers.IContainerDriver>(scope.DriverId);
            var context = new DriverContext(scope.DriverId);

            foreach (var container in containersToStart)
            {
                await container.StartAsync(cancellationToken);
                await WaitForContainerRunningAsync(driver, context, container.Id, cancellationToken);
            }
        }

        private async Task WaitForContainerRunningAsync(
            Drivers.IContainerDriver driver, DriverContext context,
            string containerId, CancellationToken cancellationToken)
        {
            const int maxAttempts = 30;
            const int delayMs = 100;
            for (int i = 0; i < maxAttempts; i++)
            {
                var inspectResult = await driver.InspectAsync(context, containerId, cancellationToken);
                if (inspectResult.Success && inspectResult.Data?.State?.Running == true)
                    return;
                await Task.Delay(delayMs, cancellationToken);
            }
        }

        private void ValidateScope()
        {
            if (_currentKernel == null || _currentDriverId == null)
            {
                throw new InvalidOperationException(
                    "Must call WithinDriver() before adding operations");
            }
        }
    }

    /// <summary>
    /// Represents a build operation to be executed.
    /// </summary>
    internal class BuildOperation
    {
        public FluentDockerKernel Kernel { get; set; }
        public string DriverId { get; set; }
        public Func<CancellationToken, Task<IService>> ExecuteAsync { get; set; }
    }

    /// <summary>
    /// Interface for the v3.0.0 fluent builder.
    /// </summary>
    public interface IBuilder
    {
        Builder WithinDriver(string driverId, FluentDockerKernel kernel = null);
        Builder UseContainer(Action<IContainerBuilder> configure);
        Builder UseNetwork(Action<INetworkBuilder> configure);
        Builder UseVolume(Action<IVolumeBuilder> configure);
        Builder UseCompose(Action<IComposeBuilder> configure);

        /// <summary>
        /// Adds an image build operation.
        /// </summary>
        Builder UseImage(string imageName, Action<DockerfileBuilder> configure);

        /// <summary>
        /// Builds all operations synchronously (TERMINAL operation).
        /// For async contexts, prefer BuildAsync() to avoid deadlocks.
        /// </summary>
        BuildResults Build();

        /// <summary>
        /// Builds all operations asynchronously (TERMINAL operation).
        /// </summary>
        Task<BuildResults> BuildAsync(CancellationToken cancellationToken = default);
    }
}
