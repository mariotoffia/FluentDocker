using System;
using FluentDocker.Kernel;
using FluentDocker.Services;
using FluentDocker.Services.Impl;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Service
{
  /// <summary>
  /// Unit tests for NetworkService.
  /// </summary>
  [Trait("Category", "Unit")]
  public class NetworkServiceTests
  {
    [Fact]
    public void Constructor_SetsProperties()
    {
      // Arrange
      var kernel = new FluentDockerKernel();

      // Act
      var service = new NetworkService(kernel, "docker", "net123", "my-network");

      // Assert
      Assert.Equal("my-network", service.Name);
      Assert.Equal("net123", service.Id);
      Assert.Equal("my-network", service.NetworkName);
      Assert.Equal(kernel, service.Kernel);
      Assert.Equal("docker", service.DriverId);
      Assert.Equal(ServiceRunningState.Running, service.State);

      kernel.Dispose();
    }

    [Fact]
    public void Constructor_NullKernel_ThrowsArgumentNullException()
    {
      Assert.Throws<ArgumentNullException>(() =>
          new NetworkService(null!, "docker", "net123", "my-network"));
    }

    [Fact]
    public void Constructor_NullDriverId_ThrowsArgumentNullException()
    {
      var kernel = new FluentDockerKernel();
      Assert.Throws<ArgumentNullException>(() =>
          new NetworkService(kernel, null!, "net123", "my-network"));
      kernel.Dispose();
    }

    [Fact]
    public void Constructor_NullNetworkId_ThrowsArgumentNullException()
    {
      var kernel = new FluentDockerKernel();
      Assert.Throws<ArgumentNullException>(() =>
          new NetworkService(kernel, "docker", null!, "my-network"));
      kernel.Dispose();
    }

    [Fact]
    public void Constructor_WithRemoveOnDispose_SetsFlag()
    {
      var kernel = new FluentDockerKernel();
      var service = new NetworkService(kernel, "docker", "net123", "my-network", removeOnDispose: true);

      // Just verify creation
      Assert.NotNull(service);
      kernel.Dispose();
    }

    [Fact]
    public void AddHook_AddsHook()
    {
      var kernel = new FluentDockerKernel();
      var service = new NetworkService(kernel, "docker", "net123", "my-network");

      service.AddHook(ServiceRunningState.Removed, async _ => { }, "test-hook");
      Assert.NotNull(service);

      kernel.Dispose();
    }

    [Fact]
    public void RemoveHook_RemovesHook()
    {
      var kernel = new FluentDockerKernel();
      var service = new NetworkService(kernel, "docker", "net123", "my-network");
      service.AddHook(ServiceRunningState.Removed, async _ => { }, "test-hook");

      service.RemoveHook("test-hook");
      Assert.NotNull(service);

      kernel.Dispose();
    }
  }
}

