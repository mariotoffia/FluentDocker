using System;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Kernel;
using FluentDocker.Model.Kernel;
using FluentDocker.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FluentDocker.MsTest
{
    /// <summary>
    /// Base class for MsTest tests that need Docker containers.
    /// </summary>
    public abstract class FluentDockerTestBase
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
                .WithDriver(DriverId, driver => driver.UseDockerCli().AsDefault())
                .BuildAsync();
        }

        [TestInitialize]
        public void Initialize()
        {
            InitializeAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Async initialization.
        /// </summary>
        protected async Task InitializeAsync()
        {
            Kernel = await CreateKernelAsync();

            OnBeforeContainerBuild();

            var builder = new Builder();
            builder.WithinDriver(DriverId, Kernel);
            builder.UseContainer(ConfigureContainer);

            OnBeforeContainerStart();

            var results = await builder.BuildAsync();
            if (results.All.Count > 0 && results.All[0] is IContainerService container)
            {
                Container = container;
                try
                {
                    await Container.StartAsync();
                }
                catch (Exception ex)
                {
                    OnBeforeDispose(Container, ex);
                    Container.Dispose();
                    throw;
                }
            }

            await OnContainerInitializedAsync();
        }

        [TestCleanup]
        public void TeardownContainer()
        {
            TeardownContainerAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Async teardown.
        /// </summary>
        protected async Task TeardownContainerAsync()
        {
            await OnContainerTearDownAsync();

            var c = Container;
            Container = null;
            if (c != null)
            {
                try
                {
                    OnBeforeDispose(c, null);
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
        /// Invoked just before the container is built.
        /// </summary>
        protected virtual void OnBeforeContainerBuild()
        {
        }

        /// <summary>
        /// Invoked just after the container is built and before starting it.
        /// </summary>
        protected virtual void OnBeforeContainerStart()
        {
        }

        /// <summary>
        /// Invoked just before the container is disposed.
        /// </summary>
        protected virtual void OnBeforeDispose(IContainerService container, Exception throwable)
        {
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
