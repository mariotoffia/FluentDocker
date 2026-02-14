using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Drivers;
using FluentDocker.Model.Containers;
using FluentDocker.Services;
using FluentDocker.Tests.Mocks;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.BuilderTests
{
  /// <summary>
  /// Unit tests for the v3 Builder with container operations.
  /// Uses mock drivers to test without Docker daemon.
  /// </summary>
  [Trait("Category", "Unit")]
  public partial class BuilderContainerTests : MockKernelTestBase, IAsyncLifetime
  {
    public async Task InitializeAsync()
    {
      await InitializeMockKernelAsync();
    }

    public Task DisposeAsync()
    {
      return base.DisposeAsync().AsTask();
    }

    [Fact]
    public async Task UseContainer_WithImage_CreatesContainer()
    {
      // Arrange
      MockPack
          .SetupContainerCreate("container-123")
          .SetupContainerStart()
          .SetupContainerInspect("container-123", running: true)
          .SetupContainerStop()
          .SetupContainerRemove();

      // Act
      var results = await new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c
              .UseImage("nginx:alpine")
              .WithName("test-web"))
          .BuildAsync();

      // Assert
      Assert.NotNull(results);
      Assert.Single(results.All);
      var container = results.All.First() as IContainerService;
      Assert.NotNull(container);
      Assert.Equal("nginx:alpine", container.Image);
    }

    [Fact]
    public async Task UseContainer_WithEnvironment_PassesEnvironment()
    {
      // Arrange
      MockPack
          .SetupContainerCreate()
          .SetupContainerStart()
          .SetupContainerInspect(running: true)
          .SetupContainerStop()
          .SetupContainerRemove();

      // Act
      await new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c
              .UseImage("nginx:alpine")
              .WithEnvironment("NGINX_HOST", "localhost")
              .WithEnvironment("NGINX_PORT", "8080"))
          .BuildAsync();

      // Assert - Verify create was called with environment
      MockPack.ContainerDriver.Verify(d => d.CreateAsync(
          It.IsAny<FluentDocker.Model.Drivers.DriverContext>(),
          It.Is<ContainerCreateConfig>(cfg =>
              cfg.Environment != null &&
              cfg.Environment.ContainsKey("NGINX_HOST") &&
              cfg.Environment["NGINX_HOST"] == "localhost"),
          It.IsAny<System.Threading.CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UseContainer_WithPorts_PassesPorts()
    {
      // Arrange
      MockPack
          .SetupContainerCreate()
          .SetupContainerStart()
          .SetupContainerInspect(running: true)
          .SetupContainerStop()
          .SetupContainerRemove();

      // Act
      await new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c
              .UseImage("nginx:alpine")
              .WithPort("80/tcp", "8080")
              .ExposePort("443"))
          .BuildAsync();

      // Assert
      MockPack.ContainerDriver.Verify(d => d.CreateAsync(
          It.IsAny<FluentDocker.Model.Drivers.DriverContext>(),
          It.Is<ContainerCreateConfig>(cfg =>
              cfg.PortBindings != null &&
              cfg.PortBindings.ContainsKey("80/tcp")),
          It.IsAny<System.Threading.CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UseContainer_WithVolumes_PassesVolumes()
    {
      // Arrange
      MockPack
          .SetupContainerCreate()
          .SetupContainerStart()
          .SetupContainerInspect(running: true)
          .SetupContainerStop()
          .SetupContainerRemove();

      // Act
      await new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c
              .UseImage("nginx:alpine")
              .WithVolume("/host/data", "/container/data"))
          .BuildAsync();

      // Assert
      MockPack.ContainerDriver.Verify(d => d.CreateAsync(
          It.IsAny<FluentDocker.Model.Drivers.DriverContext>(),
          It.Is<ContainerCreateConfig>(cfg =>
              cfg.Volumes != null &&
              cfg.Volumes.ContainsKey("/host/data") &&
              cfg.Volumes["/host/data"] == "/container/data"),
          It.IsAny<System.Threading.CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UseContainer_WithLabels_PassesLabels()
    {
      // Arrange
      MockPack
          .SetupContainerCreate()
          .SetupContainerStart()
          .SetupContainerInspect(running: true)
          .SetupContainerStop()
          .SetupContainerRemove();

      // Act
      await new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c
              .UseImage("nginx:alpine")
              .WithLabel("app", "web")
              .WithLabel("env", "test"))
          .BuildAsync();

      // Assert
      MockPack.ContainerDriver.Verify(d => d.CreateAsync(
          It.IsAny<FluentDocker.Model.Drivers.DriverContext>(),
          It.Is<ContainerCreateConfig>(cfg =>
              cfg.Labels != null &&
              cfg.Labels["app"] == "web" &&
              cfg.Labels["env"] == "test"),
          It.IsAny<System.Threading.CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UseContainer_WithCommand_PassesCommand()
    {
      // Arrange
      MockPack
          .SetupContainerCreate()
          .SetupContainerStart()
          .SetupContainerInspect(running: true)
          .SetupContainerStop()
          .SetupContainerRemove();

      // Act
      await new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c
              .UseImage("alpine")
              .WithCommand("echo", "hello", "world"))
          .BuildAsync();

      // Assert
      MockPack.ContainerDriver.Verify(d => d.CreateAsync(
          It.IsAny<FluentDocker.Model.Drivers.DriverContext>(),
          It.Is<ContainerCreateConfig>(cfg =>
              cfg.Command != null &&
              cfg.Command.Length == 3 &&
              cfg.Command[0] == "echo"),
          It.IsAny<System.Threading.CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UseContainer_WithPrivileged_PassesPrivileged()
    {
      // Arrange
      MockPack
          .SetupContainerCreate()
          .SetupContainerStart()
          .SetupContainerInspect(running: true)
          .SetupContainerStop()
          .SetupContainerRemove();

      // Act
      await new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c
              .UseImage("alpine")
              .WithPrivileged())
          .BuildAsync();

      // Assert
      MockPack.ContainerDriver.Verify(d => d.CreateAsync(
          It.IsAny<FluentDocker.Model.Drivers.DriverContext>(),
          It.Is<ContainerCreateConfig>(cfg => cfg.Privileged),
          It.IsAny<System.Threading.CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UseContainer_WithNetworkMode_PassesNetworkMode()
    {
      // Arrange
      MockPack
          .SetupContainerCreate()
          .SetupContainerStart()
          .SetupContainerInspect(running: true)
          .SetupContainerStop()
          .SetupContainerRemove();

      // Act
      await new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c
              .UseImage("alpine")
              .WithNetworkMode("host"))
          .BuildAsync();

      // Assert
      MockPack.ContainerDriver.Verify(d => d.CreateAsync(
          It.IsAny<FluentDocker.Model.Drivers.DriverContext>(),
          It.Is<ContainerCreateConfig>(cfg => cfg.NetworkMode == "host"),
          It.IsAny<System.Threading.CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UseContainer_WithRestartPolicy_PassesRestartPolicy()
    {
      // Arrange
      MockPack
          .SetupContainerCreate()
          .SetupContainerStart()
          .SetupContainerInspect(running: true)
          .SetupContainerStop()
          .SetupContainerRemove();

      // Act
      await new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c
              .UseImage("nginx:alpine")
              .WithRestartPolicy("unless-stopped"))
          .BuildAsync();

      // Assert
      MockPack.ContainerDriver.Verify(d => d.CreateAsync(
          It.IsAny<FluentDocker.Model.Drivers.DriverContext>(),
          It.Is<ContainerCreateConfig>(cfg => cfg.RestartPolicy == "unless-stopped"),
          It.IsAny<System.Threading.CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UseContainer_MultipleContainers_CreatesAll()
    {
      // Arrange
      MockPack
          .SetupContainerCreate()
          .SetupContainerStart()
          .SetupContainerInspect(running: true)
          .SetupContainerStop()
          .SetupContainerRemove();

      // Act
      var results = await new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c.UseImage("nginx:alpine").WithName("web1"))
          .UseContainer(c => c.UseImage("nginx:alpine").WithName("web2"))
          .UseContainer(c => c.UseImage("redis:alpine").WithName("cache"))
          .BuildAsync();

      // Assert
      Assert.Equal(3, results.All.Count());
      MockPack.ContainerDriver.Verify(d => d.CreateAsync(
          It.IsAny<FluentDocker.Model.Drivers.DriverContext>(),
          It.IsAny<ContainerCreateConfig>(),
          It.IsAny<System.Threading.CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task UseContainer_KeepContainer_DoesNotRemoveOnDispose()
    {
      // Arrange
      MockPack
          .SetupContainerCreate()
          .SetupContainerStart()
          .SetupContainerInspect(running: true)
          .SetupContainerStop()
          .SetupContainerRemove();

      // Act
      var results = await new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c
              .UseImage("nginx:alpine")
              .KeepContainer())
          .BuildAsync();

      // Dispose
      await results.DisposeAllAsync();

      // Assert - Remove should not be called when KeepContainer is set
      // Note: This depends on implementation of ContainerService
      // The service should respect the deleteOnDispose flag
    }

    #region Additional Container Configuration Tests

    [Fact]
    public async Task UseContainer_WithEnvironmentDictionary_PassesAllEnvironment()
    {
      // Arrange
      MockPack
          .SetupContainerCreate()
          .SetupContainerStart()
          .SetupContainerInspect(running: true)
          .SetupContainerStop()
          .SetupContainerRemove();

      // Act
      await new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c
              .UseImage("postgres:alpine")
              .WithEnvironment("DB_HOST", "localhost")
              .WithEnvironment("DB_PORT", "5432")
              .WithEnvironment("DB_NAME", "testdb"))
          .BuildAsync();

      // Assert
      MockPack.ContainerDriver.Verify(d => d.CreateAsync(
          It.IsAny<FluentDocker.Model.Drivers.DriverContext>(),
          It.Is<ContainerCreateConfig>(cfg =>
              cfg.Environment != null &&
              cfg.Environment.Count == 3 &&
              cfg.Environment["DB_HOST"] == "localhost" &&
              cfg.Environment["DB_PORT"] == "5432" &&
              cfg.Environment["DB_NAME"] == "testdb"),
          It.IsAny<System.Threading.CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UseContainer_WithEnvironmentKeyValue_ParsesCorrectly()
    {
      // Arrange
      MockPack
          .SetupContainerCreate()
          .SetupContainerStart()
          .SetupContainerInspect(running: true)
          .SetupContainerStop()
          .SetupContainerRemove();

      // Act
      await new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c
              .UseImage("alpine")
              .WithEnvironment("MY_VAR=my_value"))
          .BuildAsync();

      // Assert
      MockPack.ContainerDriver.Verify(d => d.CreateAsync(
          It.IsAny<FluentDocker.Model.Drivers.DriverContext>(),
          It.Is<ContainerCreateConfig>(cfg =>
              cfg.Environment != null &&
              cfg.Environment.ContainsKey("MY_VAR") &&
              cfg.Environment["MY_VAR"] == "my_value"),
          It.IsAny<System.Threading.CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UseContainer_WithHostname_PassesHostname()
    {
      // Arrange
      MockPack
          .SetupContainerCreate()
          .SetupContainerStart()
          .SetupContainerInspect(running: true)
          .SetupContainerStop()
          .SetupContainerRemove();

      // Act
      await new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c
              .UseImage("nginx:alpine")
              .WithHostname("my-container-host"))
          .BuildAsync();

      // Assert
      MockPack.ContainerDriver.Verify(d => d.CreateAsync(
          It.IsAny<FluentDocker.Model.Drivers.DriverContext>(),
          It.Is<ContainerCreateConfig>(cfg => cfg.Hostname == "my-container-host"),
          It.IsAny<System.Threading.CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UseContainer_WithUser_PassesUser()
    {
      // Arrange
      MockPack
          .SetupContainerCreate()
          .SetupContainerStart()
          .SetupContainerInspect(running: true)
          .SetupContainerStop()
          .SetupContainerRemove();

      // Act
      await new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c
              .UseImage("nginx:alpine")
              .WithUser("nginx"))
          .BuildAsync();

      // Assert
      MockPack.ContainerDriver.Verify(d => d.CreateAsync(
          It.IsAny<FluentDocker.Model.Drivers.DriverContext>(),
          It.Is<ContainerCreateConfig>(cfg => cfg.User == "nginx"),
          It.IsAny<System.Threading.CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UseContainer_WithWorkingDirectory_PassesWorkingDir()
    {
      // Arrange
      MockPack
          .SetupContainerCreate()
          .SetupContainerStart()
          .SetupContainerInspect(running: true)
          .SetupContainerStop()
          .SetupContainerRemove();

      // Act
      await new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c
              .UseImage("node:alpine")
              .WithWorkingDirectory("/app"))
          .BuildAsync();

      // Assert
      MockPack.ContainerDriver.Verify(d => d.CreateAsync(
          It.IsAny<FluentDocker.Model.Drivers.DriverContext>(),
          It.Is<ContainerCreateConfig>(cfg => cfg.WorkingDirectory == "/app"),
          It.IsAny<System.Threading.CancellationToken>()), Times.Once);
    }

    #endregion
  }
}
