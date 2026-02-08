using System;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Cli;
using FluentDocker.Model.Drivers;
using FluentDocker.Tests.Mocks;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver
{
    /// <summary>
    /// Tests for the IDriverInterfaceResolver implementation on driver packs.
    /// </summary>
    public class DriverInterfaceResolverTests
    {
        [Fact]
        public async Task TryResolve_KnownInterface_ReturnsTrue()
        {
            // Arrange
            var pack = new MockDriverPack();
            await pack.InitializeAsync(new DriverContext("test"));

            // Act
            var result = pack.TryResolve(typeof(IContainerDriver), out var impl);

            // Assert
            Assert.True(result);
            Assert.NotNull(impl);
            Assert.IsAssignableFrom<IContainerDriver>(impl);
        }

        [Fact]
        public async Task TryResolve_UnknownInterface_ReturnsFalse()
        {
            // Arrange
            var pack = new MockDriverPack();
            await pack.InitializeAsync(new DriverContext("test"));

            // Act
            var result = pack.TryResolve(typeof(IDisposable), out var impl);

            // Assert
            Assert.False(result);
            Assert.Null(impl);
        }

        [Fact]
        public async Task GetSupportedInterfaces_ReturnsAllRegistered()
        {
            // Arrange
            var pack = new MockDriverPack();
            await pack.InitializeAsync(new DriverContext("test"));

            // Act
            var interfaces = pack.GetSupportedInterfaces();

            // Assert - MockDriverPack registers 6 interfaces
            Assert.Equal(6, interfaces.Count);
            Assert.Contains(typeof(IContainerDriver), interfaces);
            Assert.Contains(typeof(IImageDriver), interfaces);
            Assert.Contains(typeof(INetworkDriver), interfaces);
            Assert.Contains(typeof(IVolumeDriver), interfaces);
            Assert.Contains(typeof(ISystemDriver), interfaces);
            Assert.Contains(typeof(IComposeDriver), interfaces);
        }

        [Fact]
        public void TryResolve_BeforeInitialize_ThrowsInvalidOperation()
        {
            // Arrange
            var pack = new MockDriverPack();

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() =>
                pack.TryResolve(typeof(IContainerDriver), out _));
        }

        [Fact]
        public void GetSupportedInterfaces_BeforeInitialize_ThrowsInvalidOperation()
        {
            // Arrange
            var pack = new MockDriverPack();

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() =>
                pack.GetSupportedInterfaces());
        }

        [Fact]
        public async Task TryResolve_CustomInterface_AfterRegistration_ReturnsTrue()
        {
            // Arrange
            var pack = new MockDriverPack();
            await pack.InitializeAsync(new DriverContext("test"));

            var mockCustom = new Moq.Mock<ICustomTestDriver>();
            pack.RegisterCustomDriver(mockCustom.Object);

            // Act
            var result = pack.TryResolve(typeof(ICustomTestDriver), out var impl);

            // Assert
            Assert.True(result);
            Assert.NotNull(impl);
            Assert.IsAssignableFrom<ICustomTestDriver>(impl);
        }

        /// <summary>
        /// Test interface for verifying custom driver registration.
        /// </summary>
        public interface ICustomTestDriver
        {
            void DoSomething();
        }
    }
}
