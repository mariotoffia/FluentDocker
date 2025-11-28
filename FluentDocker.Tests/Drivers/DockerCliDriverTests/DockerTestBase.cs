using System;
using System.Linq;
using System.Threading.Tasks;
using FluentDocker.Kernel;
using FluentDocker.Services;
using Xunit;

namespace FluentDocker.Tests.Drivers.DockerCliDriverTests
{
    /// <summary>
    /// Base class for Docker CLI driver integration tests.
    /// Provides kernel setup and teardown.
    /// </summary>
    public abstract class DockerTestBase : IAsyncLifetime
    {
        protected FluentDockerKernel Kernel { get; private set; }
        protected string DriverId { get; private set; }

        public virtual async Task InitializeAsync()
        {
            Kernel = await FluentDockerKernel.Create()
                .WithDriver("docker", d => d.UseDockerCli().AsDefault())
                .BuildAsync();
            DriverId = Kernel.DefaultDriverId;
        }

        public virtual Task DisposeAsync()
        {
            Kernel?.Dispose();
            return Task.CompletedTask;
        }

        /// <summary>
        /// Gets container services from build results.
        /// </summary>
        protected IContainerService GetContainer(FluentDocker.Model.Kernel.BuildResults results, int index = 0)
        {
            var containers = results.All.OfType<IContainerService>().ToList();
            return containers.Count > index ? containers[index] : null;
        }

        /// <summary>
        /// Gets container services by name predicate from build results.
        /// </summary>
        protected IContainerService GetContainer(FluentDocker.Model.Kernel.BuildResults results, Func<string, bool> namePredicate)
        {
            return results.All.OfType<IContainerService>()
                .FirstOrDefault(c => namePredicate(c.Name));
        }

        /// <summary>
        /// Gets network services from build results.
        /// </summary>
        protected INetworkService GetNetwork(FluentDocker.Model.Kernel.BuildResults results, int index = 0)
        {
            var networks = results.All.OfType<INetworkService>().ToList();
            return networks.Count > index ? networks[index] : null;
        }

        /// <summary>
        /// Gets volume services from build results.
        /// </summary>
        protected IVolumeService GetVolume(FluentDocker.Model.Kernel.BuildResults results, int index = 0)
        {
            var volumes = results.All.OfType<IVolumeService>().ToList();
            return volumes.Count > index ? volumes[index] : null;
        }

        /// <summary>
        /// Gets compose services from build results.
        /// </summary>
        protected IComposeService GetCompose(FluentDocker.Model.Kernel.BuildResults results, int index = 0)
        {
            var composes = results.All.OfType<IComposeService>().ToList();
            return composes.Count > index ? composes[index] : null;
        }
    }
}

