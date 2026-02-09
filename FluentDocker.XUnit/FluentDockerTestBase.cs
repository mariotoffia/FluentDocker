using System;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Kernel;
using FluentDocker.Model.Kernel;
using FluentDocker.Services;

namespace FluentDocker.XUnit
{
    /// <summary>
    /// Base class for XUnit tests that need Docker containers.
    /// </summary>
    public abstract class FluentDockerTestBase : IDisposable, IAsyncDisposable
    {
        protected IContainerService Container { get; private set; }
        protected FluentDockerKernel Kernel { get; private set; }
        protected string DriverId { get; private set; } = "docker-cli";

        /// <summary>
        /// Override to configure the container.
        /// </summary>
        protected abstract void ConfigureContainer(IContainerBuilder builder);

        /// <summary>
        /// Override to customize the kernel setup.
        /// </summary>
        protected virtual async Task<FluentDockerKernel> CreateKernelAsync()
        {
            return await FluentDockerKernel.Create()
                .WithDockerCli(DriverId, d => d.AsDefault())
                .BuildAsync();
        }

        /// <summary>
        /// Initializes the test. Call this in your test constructor or setup.
        /// </summary>
        protected async Task InitializeAsync()
        {
            Kernel = await CreateKernelAsync();

            var builder = new Builder();
            builder.WithinDriver(DriverId, Kernel);
            builder.UseContainer(ConfigureContainer);

            var results = await builder.BuildAsync();
            if (results.All.Count > 0 && results.All[0] is IContainerService container)
            {
                Container = container;
                await Container.StartAsync();
            }

            await OnContainerInitializedAsync();
        }

        /// <summary>
        /// Synchronous initialization for backward compatibility.
        /// </summary>
        protected void Initialize()
        {
            InitializeAsync().GetAwaiter().GetResult();
        }

        public void Dispose()
        {
            DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        public async ValueTask DisposeAsync()
        {
            await OnContainerTearDownAsync();

            var c = Container;
            Container = null;
            if (c != null)
            {
                try
                {
                    await c.StopAsync();
                    await c.RemoveAsync(force: true);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }

            Kernel?.Dispose();
            Kernel = null;
        }

        /// <summary>
        /// Invoked just before the container is torn down.
        /// </summary>
        protected virtual Task OnContainerTearDownAsync()
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// Invoked after a container has been created and started.
        /// </summary>
        protected virtual Task OnContainerInitializedAsync()
        {
            return Task.CompletedTask;
        }
    }
}
