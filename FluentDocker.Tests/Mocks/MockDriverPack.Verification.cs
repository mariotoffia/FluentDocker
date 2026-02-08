using System.Threading;
using FluentDocker.Drivers;
using FluentDocker.Model.Containers;
using FluentDocker.Model.Drivers;
using Moq;

namespace FluentDocker.Tests.Mocks
{
    /// <summary>
    /// Verification helpers for MockDriverPack.
    /// </summary>
    public partial class MockDriverPack
    {
        /// <summary>
        /// Verifies ContainerDriver.CreateAsync was called with specific image.
        /// </summary>
        public void VerifyContainerCreated(string image, Times times)
        {
            ContainerDriver.Verify(d => d.CreateAsync(
                It.IsAny<DriverContext>(),
                It.Is<ContainerCreateConfig>(c => c.Image == image),
                It.IsAny<CancellationToken>()), times);
        }

        /// <summary>
        /// Verifies ContainerDriver.RunAsync was called with specific image.
        /// </summary>
        public void VerifyContainerRun(string image, Times times)
        {
            ContainerDriver.Verify(d => d.RunAsync(
                It.IsAny<DriverContext>(),
                It.Is<ContainerCreateConfig>(c => c.Image == image),
                It.IsAny<CancellationToken>()), times);
        }

        /// <summary>
        /// Verifies ContainerDriver.StartAsync was called.
        /// </summary>
        public void VerifyContainerStarted(Times times)
        {
            ContainerDriver.Verify(d => d.StartAsync(
                It.IsAny<DriverContext>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()), times);
        }

        /// <summary>
        /// Verifies ContainerDriver.StopAsync was called.
        /// </summary>
        public void VerifyContainerStopped(Times times)
        {
            ContainerDriver.Verify(d => d.StopAsync(
                It.IsAny<DriverContext>(),
                It.IsAny<string>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()), times);
        }

        /// <summary>
        /// Verifies ContainerDriver.RemoveAsync was called.
        /// </summary>
        public void VerifyContainerRemoved(Times times)
        {
            ContainerDriver.Verify(d => d.RemoveAsync(
                It.IsAny<DriverContext>(),
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()), times);
        }

        /// <summary>
        /// Verifies NetworkDriver.CreateAsync was called.
        /// </summary>
        public void VerifyNetworkCreated(string name, Times times)
        {
            NetworkDriver.Verify(d => d.CreateAsync(
                It.IsAny<DriverContext>(),
                It.Is<NetworkCreateConfig>(c => c.Name == name),
                It.IsAny<CancellationToken>()), times);
        }

        /// <summary>
        /// Verifies VolumeDriver.CreateAsync was called.
        /// </summary>
        public void VerifyVolumeCreated(string name, Times times)
        {
            VolumeDriver.Verify(d => d.CreateAsync(
                It.IsAny<DriverContext>(),
                It.Is<VolumeCreateConfig>(c => c.Name == name),
                It.IsAny<CancellationToken>()), times);
        }
    }
}
