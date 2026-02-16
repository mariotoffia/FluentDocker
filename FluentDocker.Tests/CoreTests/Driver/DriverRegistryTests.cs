using System;
using System.Linq;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver
{
  /// <summary>
  /// Tests for DriverRegistry class.
  /// </summary>
  [Trait("Category", "Unit")]
  public class DriverRegistryTests
  {
    [Fact]
    public async Task RegisterAsync_SingleDriver_RegistersSuccessfully()
    {
      // Arrange
      var registry = new DriverRegistry();
      var driver = new MockTestDriver(DriverType.DockerCli, RuntimeType.Docker);
      var context = new DriverContext("test-driver");

      // Act
      await registry.RegisterAsync("test-driver", driver, context, TestContext.Current.CancellationToken);

      // Assert
      Assert.True(registry.IsRegistered("test-driver"));
      var retrieved = registry.GetDriver("test-driver");
      Assert.Same(driver, retrieved);
    }

    [Fact]
    public async Task RegisterAsync_MultipleDrivers_AllRegistered()
    {
      // Arrange
      var registry = new DriverRegistry();
      var driver1 = new MockTestDriver(DriverType.DockerCli, RuntimeType.Docker);
      var driver2 = new MockTestDriver(DriverType.PodmanCli, RuntimeType.Podman);
      var context1 = new DriverContext("driver-1");
      var context2 = new DriverContext("driver-2");

      // Act
      await registry.RegisterAsync("driver-1", driver1, context1, TestContext.Current.CancellationToken);
      await registry.RegisterAsync("driver-2", driver2, context2, TestContext.Current.CancellationToken);

      // Assert
      Assert.True(registry.IsRegistered("driver-1"));
      Assert.True(registry.IsRegistered("driver-2"));
      Assert.Equal(2, registry.GetAllDriverIds().Count);
    }

    [Fact]
    public async Task RegisterAsync_DuplicateId_ThrowsException()
    {
      // Arrange
      var registry = new DriverRegistry();
      var driver = new MockTestDriver(DriverType.DockerCli, RuntimeType.Docker);
      var context = new DriverContext("test-driver");
      await registry.RegisterAsync("test-driver", driver, context, TestContext.Current.CancellationToken);

      // Act & Assert
      await Assert.ThrowsAsync<DriverException>(async () =>
          await registry.RegisterAsync("test-driver", driver, context, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Unregister_RegisteredDriver_RemovesDriver()
    {
      // Arrange
      var registry = new DriverRegistry();
      var driver = new MockTestDriver(DriverType.DockerCli, RuntimeType.Docker);
      var context = new DriverContext("test-driver");
      await registry.RegisterAsync("test-driver", driver, context, TestContext.Current.CancellationToken);

      // Act
      registry.Unregister("test-driver");

      // Assert
      Assert.False(registry.IsRegistered("test-driver"));
    }

    [Fact]
    public void GetDriver_NonExistentDriver_ThrowsException()
    {
      // Arrange
      var registry = new DriverRegistry();

      // Act & Assert
      Assert.Throws<DriverNotFoundException>(() =>
          registry.GetDriver("non-existent"));
    }

    [Fact]
    public async Task TryGetDriver_ExistingDriver_ReturnsTrue()
    {
      // Arrange
      var registry = new DriverRegistry();
      var driver = new MockTestDriver(DriverType.DockerCli, RuntimeType.Docker);
      var context = new DriverContext("test-driver");
      await registry.RegisterAsync("test-driver", driver, context, TestContext.Current.CancellationToken);

      // Act
      var result = registry.TryGetDriver("test-driver", out var retrieved);

      // Assert
      Assert.True(result);
      Assert.Same(driver, retrieved);
    }

    [Fact]
    public void TryGetDriver_NonExistentDriver_ReturnsFalse()
    {
      // Arrange
      var registry = new DriverRegistry();

      // Act
      var result = registry.TryGetDriver("non-existent", out var retrieved);

      // Assert
      Assert.False(result);
      Assert.Null(retrieved);
    }

    [Fact]
    public async Task GetDriversByType_FiltersCorrectly()
    {
      // Arrange
      var registry = new DriverRegistry();
      var cliDriver = new MockTestDriver(DriverType.DockerCli, RuntimeType.Docker);
      var apiDriver = new MockTestDriver(DriverType.DockerApi, RuntimeType.Docker);
      await registry.RegisterAsync("cli-1", cliDriver, new DriverContext("cli-1"), TestContext.Current.CancellationToken);
      await registry.RegisterAsync("api-1", apiDriver, new DriverContext("api-1"), TestContext.Current.CancellationToken);

      // Act
      var cliDrivers = registry.GetDriversByType(DriverType.DockerCli);

      // Assert
      Assert.Single(cliDrivers);
      Assert.Contains("cli-1", cliDrivers);
    }

    [Fact]
    public async Task GetDriversByRuntime_FiltersCorrectly()
    {
      // Arrange
      var registry = new DriverRegistry();
      var dockerDriver = new MockTestDriver(DriverType.DockerCli, RuntimeType.Docker);
      var podmanDriver = new MockTestDriver(DriverType.PodmanCli, RuntimeType.Podman);
      await registry.RegisterAsync("docker-1", dockerDriver, new DriverContext("docker-1"), TestContext.Current.CancellationToken);
      await registry.RegisterAsync("podman-1", podmanDriver, new DriverContext("podman-1"), TestContext.Current.CancellationToken);

      // Act
      var dockerDrivers = registry.GetDriversByRuntime(RuntimeType.Docker);

      // Assert
      Assert.Single(dockerDrivers);
      Assert.Contains("docker-1", dockerDrivers);
    }

    [Fact]
    public async Task DefaultDriver_FirstRegistered_SetAsDefault()
    {
      // Arrange
      var registry = new DriverRegistry();
      var driver = new MockTestDriver(DriverType.DockerCli, RuntimeType.Docker);
      var context = new DriverContext("test-driver");

      // Act
      await registry.RegisterAsync("test-driver", driver, context, TestContext.Current.CancellationToken);

      // Assert
      Assert.Equal("test-driver", registry.GetDefaultDriverId());
    }

    [Fact]
    public async Task SetDefaultDriver_ChangesDefault()
    {
      // Arrange
      var registry = new DriverRegistry();
      var driver1 = new MockTestDriver(DriverType.DockerCli, RuntimeType.Docker);
      var driver2 = new MockTestDriver(DriverType.DockerCli, RuntimeType.Docker);
      await registry.RegisterAsync("driver-1", driver1, new DriverContext("driver-1"), TestContext.Current.CancellationToken);
      await registry.RegisterAsync("driver-2", driver2, new DriverContext("driver-2"), TestContext.Current.CancellationToken);

      // Act
      registry.SetDefaultDriver("driver-2");

      // Assert
      Assert.Equal("driver-2", registry.GetDefaultDriverId());
    }

    [Fact]
    public async Task SetDefaultDriver_NonExistentDriver_ThrowsException()
    {
      // Arrange
      var registry = new DriverRegistry();
      var driver = new MockTestDriver(DriverType.DockerCli, RuntimeType.Docker);
      await registry.RegisterAsync("driver-1", driver, new DriverContext("driver-1"), TestContext.Current.CancellationToken);

      // Act & Assert
      Assert.Throws<DriverNotFoundException>(() =>
          registry.SetDefaultDriver("non-existent"));
    }

    [Fact]
    public async Task GetContext_ReturnsCorrectContext()
    {
      // Arrange
      var registry = new DriverRegistry();
      var driver = new MockTestDriver(DriverType.DockerCli, RuntimeType.Docker);
      var context = new DriverContext("test-driver")
      {
        Host = "unix:///var/run/docker.sock"
      };
      await registry.RegisterAsync("test-driver", driver, context, TestContext.Current.CancellationToken);

      // Act
      var retrieved = registry.GetContext("test-driver");

      // Assert
      Assert.Equal("unix:///var/run/docker.sock", retrieved.Host);
    }

    // Mock driver for testing
    private class MockTestDriver : IDriver
    {
      public MockTestDriver(DriverType type, RuntimeType runtime)
      {
        Type = type;
        Runtime = runtime;
      }

      public DriverType Type { get; }
      public RuntimeType Runtime { get; }

      public Task<DriverCapabilities> GetCapabilitiesAsync(System.Threading.CancellationToken cancellationToken = default)
      {
        return Task.FromResult(DriverCapabilities.Default());
      }

      public Task<bool> IsHealthyAsync(System.Threading.CancellationToken cancellationToken = default)
      {
        return Task.FromResult(true);
      }

      public Task InitializeAsync(DriverContext context, System.Threading.CancellationToken cancellationToken = default)
      {
        return Task.CompletedTask;
      }
    }
  }
}

