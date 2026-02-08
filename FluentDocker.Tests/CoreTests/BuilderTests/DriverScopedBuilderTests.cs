using System;
using System.Linq;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Podman;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using FluentDocker.Tests.Mocks;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.BuilderTests
{
    /// <summary>
    /// Tests for IDriverScopedBuilder implementation and extension methods
    /// across all builder types (Container, Network, Volume, Compose, Image).
    /// </summary>
    public class DriverScopedBuilderTests
    {
        private async Task<(FluentDockerKernel kernel, MockDriverPack pack)> CreateKernelWithMockPack()
        {
            var pack = new MockDriverPack();
            var kernel = new FluentDockerKernel();
            await kernel.RegisterDriverPackAsync("test", pack, new DriverContext("test"));
            return (kernel, pack);
        }

        #region Builder IDriverScopedBuilder Implementation

        [Fact]
        public async Task ContainerBuilder_ImplementsIDriverScopedBuilder()
        {
            // Arrange
            var (kernel, _) = await CreateKernelWithMockPack();
            IDriverScopedBuilder scoped = null;

            // Act - capture the builder via the configure lambda
            new Builder()
                .WithinDriver("test", kernel)
                .UseContainer(cb =>
                {
                    Assert.IsAssignableFrom<IDriverScopedBuilder>(cb);
                    scoped = (IDriverScopedBuilder)cb;
                });

            // Assert
            Assert.NotNull(scoped);
            Assert.Same(kernel, scoped.Kernel);
            Assert.Equal("test", scoped.DriverId);
        }

        [Fact]
        public async Task NetworkBuilder_ImplementsIDriverScopedBuilder()
        {
            var (kernel, _) = await CreateKernelWithMockPack();
            IDriverScopedBuilder scoped = null;

            new Builder()
                .WithinDriver("test", kernel)
                .UseNetwork(nb =>
                {
                    Assert.IsAssignableFrom<IDriverScopedBuilder>(nb);
                    scoped = (IDriverScopedBuilder)nb;
                });

            Assert.NotNull(scoped);
            Assert.Same(kernel, scoped.Kernel);
            Assert.Equal("test", scoped.DriverId);
        }

        [Fact]
        public async Task VolumeBuilder_ImplementsIDriverScopedBuilder()
        {
            var (kernel, _) = await CreateKernelWithMockPack();
            IDriverScopedBuilder scoped = null;

            new Builder()
                .WithinDriver("test", kernel)
                .UseVolume(vb =>
                {
                    Assert.IsAssignableFrom<IDriverScopedBuilder>(vb);
                    scoped = (IDriverScopedBuilder)vb;
                });

            Assert.NotNull(scoped);
            Assert.Same(kernel, scoped.Kernel);
            Assert.Equal("test", scoped.DriverId);
        }

        [Fact]
        public async Task ComposeBuilder_ImplementsIDriverScopedBuilder()
        {
            var (kernel, _) = await CreateKernelWithMockPack();
            IDriverScopedBuilder scoped = null;

            new Builder()
                .WithinDriver("test", kernel)
                .UseCompose(cb =>
                {
                    Assert.IsAssignableFrom<IDriverScopedBuilder>(cb);
                    scoped = (IDriverScopedBuilder)cb;
                });

            Assert.NotNull(scoped);
            Assert.Same(kernel, scoped.Kernel);
            Assert.Equal("test", scoped.DriverId);
        }

        [Fact]
        public void ImageBuilder_TypeImplementsIDriverScopedBuilder()
        {
            // ImageBuilder is internal, so we verify via reflection that
            // it implements IDriverScopedBuilder.
            var imageBuilderType = typeof(Builder).Assembly
                .GetTypes()
                .FirstOrDefault(t => t.Name == "ImageBuilder" && !t.IsInterface);

            Assert.NotNull(imageBuilderType);
            Assert.True(
                typeof(IDriverScopedBuilder).IsAssignableFrom(imageBuilderType),
                "ImageBuilder should implement IDriverScopedBuilder");
        }

        #endregion

        #region RequireDriver Extension

        [Fact]
        public async Task RequireDriver_KnownInterface_ReturnsInstance()
        {
            var (kernel, _) = await CreateKernelWithMockPack();

            new Builder()
                .WithinDriver("test", kernel)
                .UseContainer(cb =>
                {
                    var scoped = (IDriverScopedBuilder)cb;
                    var driver = scoped.RequireDriver<IContainerDriver>();
                    Assert.NotNull(driver);
                });
        }

        [Fact]
        public async Task RequireDriver_UnknownInterface_ThrowsInterfaceNotSupported()
        {
            var (kernel, _) = await CreateKernelWithMockPack();

            new Builder()
                .WithinDriver("test", kernel)
                .UseContainer(cb =>
                {
                    var scoped = (IDriverScopedBuilder)cb;
                    Assert.Throws<InterfaceNotSupportedException>(() =>
                        scoped.RequireDriver<IPodmanPodDriver>());
                });
        }

        #endregion

        #region TryDriver Extension

        [Fact]
        public async Task TryDriver_KnownInterface_ReturnsInstance()
        {
            var (kernel, _) = await CreateKernelWithMockPack();

            new Builder()
                .WithinDriver("test", kernel)
                .UseContainer(cb =>
                {
                    var scoped = (IDriverScopedBuilder)cb;
                    var driver = scoped.TryDriver<IContainerDriver>();
                    Assert.NotNull(driver);
                });
        }

        [Fact]
        public async Task TryDriver_UnknownInterface_ReturnsNull()
        {
            var (kernel, _) = await CreateKernelWithMockPack();

            new Builder()
                .WithinDriver("test", kernel)
                .UseContainer(cb =>
                {
                    var scoped = (IDriverScopedBuilder)cb;
                    var driver = scoped.TryDriver<IPodmanPodDriver>();
                    Assert.Null(driver);
                });
        }

        #endregion
    }
}
