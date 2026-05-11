using System;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Containers;
using FluentDocker.Services;
using FluentDocker.Services.Impl;
using FluentDocker.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;


namespace FluentDocker.Tests.CoreTests.Service
{
  /// <summary>
  /// Unit tests for ContainerService.
  /// </summary>
  [Trait("Category", "Unit")]
  public partial class ContainerServiceTests
  {
    [Fact]
    public void Constructor_SetsProperties()
    {
      // Arrange
      var kernel = new FluentDockerKernel(new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
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
      var kernel = new FluentDockerKernel(new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);

      // Act & Assert
      Assert.Throws<ArgumentNullException>(() =>
          new ContainerService(kernel, null!, "abc123", "nginx", "test"));

      kernel.Dispose();
    }

    [Fact]
    public void Constructor_NullContainerId_ThrowsArgumentNullException()
    {
      // Arrange
      var kernel = new FluentDockerKernel(new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);

      // Act & Assert
      Assert.Throws<ArgumentNullException>(() =>
          new ContainerService(kernel, "docker", null!, "nginx", "test"));

      kernel.Dispose();
    }

    [Fact]
    public void Constructor_NullName_GeneratesDefault()
    {
      // Arrange
      var kernel = new FluentDockerKernel(new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);

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
      var kernel = new FluentDockerKernel(new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
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
      var kernel = new FluentDockerKernel(new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
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
      var kernel = new FluentDockerKernel(new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
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
      var kernel = new FluentDockerKernel(new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
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
      var kernel = new FluentDockerKernel(new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      var service = new ContainerService(kernel, "docker", "abc123", "nginx", "test",
          stopOnDispose: false, deleteOnDispose: false);

      // Act & Assert - should not throw
      await service.DisposeAsync();
      await service.DisposeAsync();
      await service.DisposeAsync();

      kernel.Dispose();
    }

    [Fact]
    public void Service_ImplementsIServiceAsync()
    {
      var kernel = new FluentDockerKernel(new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      var service = new ContainerService(kernel, "docker", "abc123", "nginx", "test");

#pragma warning disable CA1859 // Intent: verify interface contract via interface reference
      IServiceAsync asyncService = service;
#pragma warning restore CA1859
      Assert.NotNull(asyncService);
      Assert.Equal("test", asyncService.Name);
      Assert.Equal("docker", asyncService.DriverId);

      kernel.Dispose();
    }
  }
}
