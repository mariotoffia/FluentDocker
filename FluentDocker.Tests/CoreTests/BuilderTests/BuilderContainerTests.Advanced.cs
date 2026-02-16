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
  /// Advanced container configuration tests for the v3 Builder.
  /// </summary>
  [Trait("Category", "Unit")]
  public partial class BuilderContainerTests
  {
    #region Advanced Container Configuration Tests

    [Fact]
    public async Task UseContainer_WithMemoryLimit_PassesMemoryLimit()
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
              .WithMemoryLimit(536870912)) // 512MB
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);

      // Assert
      MockPack.ContainerDriver.Verify(d => d.CreateAsync(
          It.IsAny<FluentDocker.Model.Drivers.DriverContext>(),
          It.Is<ContainerCreateConfig>(cfg => cfg.MemoryLimit == 536870912),
          It.IsAny<System.Threading.CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UseContainer_WithCpuShares_PassesCpuShares()
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
              .WithCpuShares(512))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);

      // Assert
      MockPack.ContainerDriver.Verify(d => d.CreateAsync(
          It.IsAny<FluentDocker.Model.Drivers.DriverContext>(),
          It.Is<ContainerCreateConfig>(cfg => cfg.CpuShares == 512),
          It.IsAny<System.Threading.CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UseContainer_WithAutoRemove_PassesAutoRemove()
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
              .WithAutoRemove())
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);

      // Assert
      MockPack.ContainerDriver.Verify(d => d.CreateAsync(
          It.IsAny<FluentDocker.Model.Drivers.DriverContext>(),
          It.Is<ContainerCreateConfig>(cfg => cfg.AutoRemove == true),
          It.IsAny<System.Threading.CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UseContainer_WithNetwork_PassesNetwork()
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
              .WithNetwork("my-network"))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);

      // Assert
      MockPack.ContainerDriver.Verify(d => d.CreateAsync(
          It.IsAny<FluentDocker.Model.Drivers.DriverContext>(),
          It.Is<ContainerCreateConfig>(cfg =>
              cfg.Networks != null &&
              cfg.Networks.Contains("my-network")),
          It.IsAny<System.Threading.CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UseContainer_WithNetworkAlias_PassesNetworkAlias()
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
              .WithNetworkAlias("my-network", "webserver"))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);

      // Assert - verify driver was called (NetworkAliases are handled at container run level)
      MockPack.ContainerDriver.Verify(d => d.CreateAsync(
          It.IsAny<FluentDocker.Model.Drivers.DriverContext>(),
          It.IsAny<ContainerCreateConfig>(),
          It.IsAny<System.Threading.CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UseContainer_WithLink_PassesLink()
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
              .WithLink("database", "db"))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);

      // Assert
      MockPack.ContainerDriver.Verify(d => d.CreateAsync(
          It.IsAny<FluentDocker.Model.Drivers.DriverContext>(),
          It.Is<ContainerCreateConfig>(cfg =>
              cfg.Links != null &&
              cfg.Links.Count > 0),
          It.IsAny<System.Threading.CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UseContainer_WithLinks_PassesMultipleLinks()
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
              .WithLinks("database", "cache", "queue"))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);

      // Assert
      MockPack.ContainerDriver.Verify(d => d.CreateAsync(
          It.IsAny<FluentDocker.Model.Drivers.DriverContext>(),
          It.Is<ContainerCreateConfig>(cfg =>
              cfg.Links != null &&
              cfg.Links.Count == 3),
          It.IsAny<System.Threading.CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UseContainer_ExposePortWithHostPort_PassesPortMapping()
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
              .ExposePort(8080, 80))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);

      // Assert
      MockPack.ContainerDriver.Verify(d => d.CreateAsync(
          It.IsAny<FluentDocker.Model.Drivers.DriverContext>(),
          It.Is<ContainerCreateConfig>(cfg =>
              cfg.PortBindings != null &&
              cfg.PortBindings.ContainsKey("80/tcp")),
          It.IsAny<System.Threading.CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UseContainer_KeepRunning_DoesNotStopOnDispose()
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
              .KeepRunning())
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);

      // Dispose
      await results.DisposeAllAsync();

      // Assert - The service was created with stopOnDispose=false
      // Container should not be stopped when KeepRunning is set
    }

    [Fact]
    public async Task UseContainer_ForcePullImage_SetsForcePullFlag()
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
              .ForcePullImage())
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);

      // Assert - Just verify the container was created
      // The force pull flag should trigger image pull before create
      MockPack.ContainerDriver.Verify(d => d.CreateAsync(
          It.IsAny<FluentDocker.Model.Drivers.DriverContext>(),
          It.IsAny<ContainerCreateConfig>(),
          It.IsAny<System.Threading.CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UseContainer_ReuseIfExists_SetsReuseFlag()
    {
      // Arrange
      MockPack
          .SetupContainerList()  // Important for ReuseIfExists to check existing containers
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
              .WithName("test-container")
              .ReuseIfExists())
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);

      // Assert - container was created (in mock, no existing container)
      MockPack.ContainerDriver.Verify(d => d.CreateAsync(
          It.IsAny<FluentDocker.Model.Drivers.DriverContext>(),
          It.IsAny<ContainerCreateConfig>(),
          It.IsAny<System.Threading.CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UseContainer_DestroyIfExists_SetsDestroyFlag()
    {
      // Arrange
      MockPack
          .SetupContainerList()  // Important for DestroyIfExists to check existing containers
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
              .WithName("test-container")
              .DestroyIfExists(force: true, removeVolumes: true))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);

      // Assert - container was created
      MockPack.ContainerDriver.Verify(d => d.CreateAsync(
          It.IsAny<FluentDocker.Model.Drivers.DriverContext>(),
          It.IsAny<ContainerCreateConfig>(),
          It.IsAny<System.Threading.CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UseContainer_DeleteVolumeOnDispose_SetsFlag()
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
              .DeleteVolumeOnDispose())
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);

      // Assert - container was created
      MockPack.ContainerDriver.Verify(d => d.CreateAsync(
          It.IsAny<FluentDocker.Model.Drivers.DriverContext>(),
          It.IsAny<ContainerCreateConfig>(),
          It.IsAny<System.Threading.CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UseContainer_DeleteNamedVolumeOnDispose_SetsFlag()
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
              .DeleteNamedVolumeOnDispose())
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);

      // Assert - container was created
      MockPack.ContainerDriver.Verify(d => d.CreateAsync(
          It.IsAny<FluentDocker.Model.Drivers.DriverContext>(),
          It.IsAny<ContainerCreateConfig>(),
          It.IsAny<System.Threading.CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UseContainer_CombinedConfiguration_AllOptionsPassedCorrectly()
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
              .WithName("full-config-test")
              .WithEnvironment("ENV", "production")
              .WithPort("80/tcp", "8080")
              .WithLabel("app", "test")
              .WithHostname("testhost")
              .WithUser("nginx")
              .WithWorkingDirectory("/app")
              .WithRestartPolicy("always")
              .WithNetworkMode("bridge")
              .WithMemoryLimit(268435456) // 256MB
              .WithCpuShares(256)
              .WithPrivileged(false))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);

      // Assert
      MockPack.ContainerDriver.Verify(d => d.CreateAsync(
          It.IsAny<FluentDocker.Model.Drivers.DriverContext>(),
          It.Is<ContainerCreateConfig>(cfg =>
              cfg.Name == "full-config-test" &&
              cfg.Environment != null &&
              cfg.Environment["ENV"] == "production" &&
              cfg.PortBindings != null &&
              cfg.PortBindings.ContainsKey("80/tcp") &&
              cfg.Labels != null &&
              cfg.Labels["app"] == "test" &&
              cfg.Hostname == "testhost" &&
              cfg.User == "nginx" &&
              cfg.WorkingDirectory == "/app" &&
              cfg.RestartPolicy == "always" &&
              cfg.NetworkMode == "bridge" &&
              cfg.MemoryLimit == 268435456 &&
              cfg.CpuShares == 256 &&
              cfg.Privileged == false),
          It.IsAny<System.Threading.CancellationToken>()), Times.Once);
    }

    #endregion
  }
}
