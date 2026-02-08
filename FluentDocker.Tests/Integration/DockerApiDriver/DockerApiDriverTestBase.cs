using System;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.Integration.DockerApiDriver
{
    /// <summary>
    /// Base class for Docker API driver integration tests.
    /// Builds a kernel configured with the docker-api driver pack.
    /// </summary>
    public abstract class DockerApiDriverTestBase : IAsyncLifetime
    {
        protected FluentDockerKernel Kernel { get; private set; }
        protected string DriverId => "docker-api";
        protected DriverContext Context => new DriverContext(DriverId);

        protected const string TestImage = "alpine:latest";
        protected const string BusyboxImage = "busybox:latest";

        public async Task InitializeAsync()
        {
            Kernel = await FluentDockerKernel.Create()
                .WithDriver(DriverId, d => d.UseDockerApi().AsDefault())
                .BuildAsync();
        }

        public Task DisposeAsync()
        {
            Kernel?.Dispose();
            return Task.CompletedTask;
        }

        protected T GetDriver<T>() where T : class =>
            Kernel.SysCtl<T>(DriverId);

        protected IContainerDriver ContainerDriver => GetDriver<IContainerDriver>();
        protected INetworkDriver NetworkDriver => GetDriver<INetworkDriver>();
        protected IVolumeDriver VolumeDriver => GetDriver<IVolumeDriver>();
        protected IImageDriver ImageDriver => GetDriver<IImageDriver>();
        protected ISystemDriver SystemDriver => GetDriver<ISystemDriver>();

        protected async Task EnsureImageAsync(string image)
        {
            var parts = image.Split(':');
            var name = parts[0];
            var tag = parts.Length > 1 ? parts[1] : "latest";
            await ImageDriver.PullAsync(Context, name, tag);
        }

        protected string UniqueName(string prefix = "test") =>
            $"{prefix}-{Guid.NewGuid():N}"[..20];
    }
}
