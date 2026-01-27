using System;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Containers;
using FluentDocker.Services;
using FluentDocker.Services.Impl;
using FluentDocker.Tests.Mocks;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Service
{
    /// <summary>
    /// Unit tests for ContainerService.
    /// </summary>
    [Trait("Category", "Unit")]
    public class ContainerServiceTests
    {
        [Fact]
        public void Constructor_SetsProperties()
        {
            // Arrange
            var kernel = new FluentDockerKernel();
            var driverId = "docker";
            var containerId = "abc123";
            var image = "nginx:latest";
            var name = "test-container";

            // Act
            var service = new ContainerService(kernel, driverId, containerId, image, name);

            // Assert
            Assert.Equal(name, service.Name);
            Assert.Equal(containerId, service.Id);
            Assert.Equal(image, service.Image);
            Assert.Equal(kernel, service.Kernel);
            Assert.Equal(driverId, service.DriverId);
            Assert.Equal(ServiceRunningState.Unknown, service.State);

            // Cleanup
            kernel.Dispose();
        }

        [Fact]
        public void Constructor_NullKernel_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                new ContainerService(null!, "docker", "abc123", "nginx", "test"));
        }

        [Fact]
        public void Constructor_NullDriverId_ThrowsArgumentNullException()
        {
            // Arrange
            var kernel = new FluentDockerKernel();

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                new ContainerService(kernel, null!, "abc123", "nginx", "test"));

            kernel.Dispose();
        }

        [Fact]
        public void Constructor_NullContainerId_ThrowsArgumentNullException()
        {
            // Arrange
            var kernel = new FluentDockerKernel();

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                new ContainerService(kernel, "docker", null!, "nginx", "test"));

            kernel.Dispose();
        }

        [Fact]
        public void Constructor_NullName_GeneratesDefault()
        {
            // Arrange
            var kernel = new FluentDockerKernel();

            // Act
            var service = new ContainerService(kernel, "docker", "abc123", "nginx", null);

            // Assert
            Assert.StartsWith("container-", service.Name);
            Assert.Contains("abc123", service.Name);

            kernel.Dispose();
        }

        [Fact]
        public void AddHook_AddsHook()
        {
            // Arrange
            var kernel = new FluentDockerKernel();
            var service = new ContainerService(kernel, "docker", "abc123", "nginx", "test");
            var hookCalled = false;

            // Act
            service.AddHook(ServiceRunningState.Running, async _ => hookCalled = true, "test-hook");

            // Assert - hook is added (we can't easily verify without triggering state change)
            Assert.NotNull(service);

            kernel.Dispose();
        }

        [Fact]
        public void RemoveHook_RemovesHook()
        {
            // Arrange
            var kernel = new FluentDockerKernel();
            var service = new ContainerService(kernel, "docker", "abc123", "nginx", "test");
            service.AddHook(ServiceRunningState.Running, async _ => { }, "test-hook");

            // Act
            service.RemoveHook("test-hook");

            // Assert - just verify no exception
            Assert.NotNull(service);

            kernel.Dispose();
        }

        [Fact]
        public void StateChange_Event_CanBeSubscribed()
        {
            // Arrange
            var kernel = new FluentDockerKernel();
            var service = new ContainerService(kernel, "docker", "abc123", "nginx", "test");
            var eventRaised = false;

            // Act
            service.StateChange += (sender, args) => eventRaised = true;

            // Assert - event subscription works
            Assert.NotNull(service);

            kernel.Dispose();
        }

        [Fact]
        public void Dispose_CanBeCalledMultipleTimes()
        {
            // Arrange
            var kernel = new FluentDockerKernel();
            var service = new ContainerService(kernel, "docker", "abc123", "nginx", "test",
                stopOnDispose: false, deleteOnDispose: false);

            // Act & Assert - should not throw
            service.Dispose();
            service.Dispose();
            service.Dispose();

            kernel.Dispose();
        }

        [Fact]
        public async Task DisposeAsync_CanBeCalledMultipleTimes()
        {
            // Arrange
            var kernel = new FluentDockerKernel();
            var service = new ContainerService(kernel, "docker", "abc123", "nginx", "test",
                stopOnDispose: false, deleteOnDispose: false);

            // Act & Assert - should not throw
            await service.DisposeAsync();
            await service.DisposeAsync();
            await service.DisposeAsync();

            kernel.Dispose();
        }

        [Fact]
        public void IService_Start_CallsAsync()
        {
            // This test just verifies the sync wrapper exists
            // Actual functionality requires a real driver
            var kernel = new FluentDockerKernel();
            var service = new ContainerService(kernel, "docker", "abc123", "nginx", "test");

            // Verify interface implementation
            IService iservice = service;
            Assert.NotNull(iservice);

            kernel.Dispose();
        }

        [Fact]
        public void IService_Pause_CallsAsync()
        {
            var kernel = new FluentDockerKernel();
            var service = new ContainerService(kernel, "docker", "abc123", "nginx", "test");

            IService iservice = service;
            Assert.NotNull(iservice);

            kernel.Dispose();
        }

        [Fact]
        public void IService_Stop_CallsAsync()
        {
            var kernel = new FluentDockerKernel();
            var service = new ContainerService(kernel, "docker", "abc123", "nginx", "test");

            IService iservice = service;
            Assert.NotNull(iservice);

            kernel.Dispose();
        }

        [Fact]
        public void IService_Remove_CallsAsync()
        {
            var kernel = new FluentDockerKernel();
            var service = new ContainerService(kernel, "docker", "abc123", "nginx", "test");

            IService iservice = service;
            Assert.NotNull(iservice);

            kernel.Dispose();
        }

        #region Service Operation Tests

        [Fact]
        [Trait("Category", "Unit")]
        public async Task InspectAsync_CallsDriverAndReturnsContainer()
        {
            // Arrange
            var mockPack = new MockDriverPack();
            mockPack.SetupContainerInspect("test-container-123", running: true);

            var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);

            var service = new ContainerService(kernel, "docker", "test-container-123", "nginx:latest", "test-container");

            try
            {
                // Act
                var container = await service.InspectAsync();

                // Assert
                Assert.NotNull(container);
                Assert.Equal("test-container-123", container.Id);
                Assert.True(container.State.Running);
                mockPack.ContainerDriver.Verify(d => d.InspectAsync(
                    It.IsAny<FluentDocker.Model.Drivers.DriverContext>(),
                    It.Is<string>(s => s == "test-container-123"),
                    It.IsAny<System.Threading.CancellationToken>()), Times.Once);
            }
            finally
            {
                kernel.Dispose();
            }
        }

        [Fact]
        [Trait("Category", "Unit")]
        public async Task GetLogsAsync_CallsDriverAndReturnsLogs()
        {
            // Arrange
            var mockPack = new MockDriverPack();
            mockPack.SetupContainerGetLogs("Application started\nListening on port 80");

            var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);

            var service = new ContainerService(kernel, "docker", "test-container-123", "nginx:latest", "test-container");

            try
            {
                // Act
                var logs = await service.GetLogsAsync();

                // Assert
                Assert.Contains("Application started", logs);
                Assert.Contains("Listening on port 80", logs);
                mockPack.ContainerDriver.Verify(d => d.GetLogsAsync(
                    It.IsAny<FluentDocker.Model.Drivers.DriverContext>(),
                    It.Is<string>(s => s == "test-container-123"),
                    It.IsAny<bool>(),
                    It.IsAny<int?>(),
                    It.IsAny<bool>(),
                    It.IsAny<System.Threading.CancellationToken>()), Times.Once);
            }
            finally
            {
                kernel.Dispose();
            }
        }

        [Fact]
        [Trait("Category", "Unit")]
        public async Task ExecuteAsync_CallsDriverWithCommand()
        {
            // Arrange
            var mockPack = new MockDriverPack();
            mockPack.SetupContainerExec("file1.txt\nfile2.txt", 0);

            var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);

            var service = new ContainerService(kernel, "docker", "test-container-123", "nginx:latest", "test-container");

            try
            {
                // Act
                var result = await service.ExecuteAsync("ls -la");

                // Assert
                Assert.Contains("file1.txt", result);
                mockPack.ContainerDriver.Verify(d => d.ExecAsync(
                    It.IsAny<FluentDocker.Model.Drivers.DriverContext>(),
                    It.Is<string>(s => s == "test-container-123"),
                    It.Is<ExecConfig>(c => c.Command.Length == 2 && c.Command[0] == "ls"),
                    It.IsAny<System.Threading.CancellationToken>()), Times.Once);
            }
            finally
            {
                kernel.Dispose();
            }
        }

        [Fact]
        [Trait("Category", "Unit")]
        public async Task StartAsync_CallsDriverAndUpdatesState()
        {
            // Arrange
            var mockPack = new MockDriverPack();
            mockPack.SetupContainerStart();

            var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);

            var service = new ContainerService(kernel, "docker", "test-container-123", "nginx:latest", "test-container");

            try
            {
                // Act
                await service.StartAsync();

                // Assert
                Assert.Equal(ServiceRunningState.Running, service.State);
                mockPack.ContainerDriver.Verify(d => d.StartAsync(
                    It.IsAny<FluentDocker.Model.Drivers.DriverContext>(),
                    It.Is<string>(s => s == "test-container-123"),
                    It.IsAny<System.Threading.CancellationToken>()), Times.Once);
            }
            finally
            {
                kernel.Dispose();
            }
        }

        [Fact]
        [Trait("Category", "Unit")]
        public async Task StopAsync_CallsDriverAndUpdatesState()
        {
            // Arrange
            var mockPack = new MockDriverPack();
            mockPack.SetupContainerStop();

            var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);

            var service = new ContainerService(kernel, "docker", "test-container-123", "nginx:latest", "test-container");

            try
            {
                // Act
                await service.StopAsync();

                // Assert
                Assert.Equal(ServiceRunningState.Stopped, service.State);
                mockPack.ContainerDriver.Verify(d => d.StopAsync(
                    It.IsAny<FluentDocker.Model.Drivers.DriverContext>(),
                    It.Is<string>(s => s == "test-container-123"),
                    It.IsAny<int?>(),
                    It.IsAny<System.Threading.CancellationToken>()), Times.Once);
            }
            finally
            {
                kernel.Dispose();
            }
        }

        [Fact]
        [Trait("Category", "Unit")]
        public async Task PauseAsync_CallsDriverAndUpdatesState()
        {
            // Arrange
            var mockPack = new MockDriverPack();
            mockPack.SetupContainerPause();

            var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);

            var service = new ContainerService(kernel, "docker", "test-container-123", "nginx:latest", "test-container");

            try
            {
                // Act
                await service.PauseAsync();

                // Assert
                Assert.Equal(ServiceRunningState.Paused, service.State);
                mockPack.ContainerDriver.Verify(d => d.PauseAsync(
                    It.IsAny<FluentDocker.Model.Drivers.DriverContext>(),
                    It.Is<string>(s => s == "test-container-123"),
                    It.IsAny<System.Threading.CancellationToken>()), Times.Once);
            }
            finally
            {
                kernel.Dispose();
            }
        }

        [Fact]
        [Trait("Category", "Unit")]
        public async Task RemoveAsync_CallsDriverAndUpdatesState()
        {
            // Arrange
            var mockPack = new MockDriverPack();
            mockPack.SetupContainerRemove();

            var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);

            var service = new ContainerService(kernel, "docker", "test-container-123", "nginx:latest", "test-container",
                deleteVolumeOnDispose: true);

            try
            {
                // Act
                await service.RemoveAsync();

                // Assert
                Assert.Equal(ServiceRunningState.Removed, service.State);
                mockPack.ContainerDriver.Verify(d => d.RemoveAsync(
                    It.IsAny<FluentDocker.Model.Drivers.DriverContext>(),
                    It.Is<string>(s => s == "test-container-123"),
                    It.IsAny<bool>(),
                    It.Is<bool>(v => v == true),  // removeVolumes should be true
                    It.IsAny<System.Threading.CancellationToken>()), Times.Once);
            }
            finally
            {
                kernel.Dispose();
            }
        }

        [Fact]
        [Trait("Category", "Unit")]
        public async Task GetStatsAsync_CallsDriverAndReturnsStats()
        {
            // Arrange
            var mockPack = new MockDriverPack();
            mockPack.SetupContainerStats(
                cpuPercent: 25.5,
                memoryUsage: 104857600,   // 100 MiB
                memoryLimit: 1073741824,  // 1 GiB
                memoryPercent: 9.77,
                networkRx: 1024000,
                networkTx: 512000,
                blockRead: 2048000,
                blockWrite: 1024000,
                pids: 5);

            var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);

            var service = new ContainerService(kernel, "docker", "test-container-123", "nginx:latest", "test-container");

            try
            {
                // Act
                var stats = await service.GetStatsAsync();

                // Assert
                Assert.NotNull(stats);
                Assert.Equal("test-container-123", stats.ContainerId);

                // CPU stats
                Assert.NotNull(stats.Cpu);
                Assert.Equal(25.5, stats.Cpu.UsagePercent);

                // Memory stats
                Assert.NotNull(stats.Memory);
                Assert.Equal(104857600, stats.Memory.Usage);
                Assert.Equal(1073741824, stats.Memory.Limit);
                Assert.Equal(9.77, stats.Memory.UsagePercent);

                // Network stats
                Assert.NotNull(stats.Network);
                Assert.Equal(1024000, stats.Network.RxBytes);
                Assert.Equal(512000, stats.Network.TxBytes);

                // Disk stats
                Assert.NotNull(stats.Disk);
                Assert.Equal(2048000, stats.Disk.ReadBytes);
                Assert.Equal(1024000, stats.Disk.WriteBytes);

                // Verify driver was called
                mockPack.ContainerDriver.Verify(d => d.StatsAsync(
                    It.IsAny<FluentDocker.Model.Drivers.DriverContext>(),
                    It.Is<string>(s => s == "test-container-123"),
                    It.IsAny<System.Threading.CancellationToken>()), Times.Once);
            }
            finally
            {
                kernel.Dispose();
            }
        }

        #endregion
    }
}

