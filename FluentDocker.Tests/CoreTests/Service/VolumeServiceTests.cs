using System;
using FluentDocker.Kernel;
using FluentDocker.Services;
using FluentDocker.Services.Impl;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Service
{
  /// <summary>
  /// Unit tests for VolumeService.
  /// </summary>
  [Trait("Category", "Unit")]
  public class VolumeServiceTests
  {
    [Fact]
    public void Constructor_SetsProperties()
    {
      // Arrange
      var kernel = new FluentDockerKernel();

      // Act
      var service = new VolumeService(kernel, "docker", "my-volume", "local");

      // Assert
      Assert.Equal("my-volume", service.Name);
      Assert.Equal("my-volume", service.VolumeName);
      Assert.Equal("local", service.Driver);
      Assert.Equal(kernel, service.Kernel);
      Assert.Equal("docker", service.DriverId);
      Assert.Equal(ServiceRunningState.Running, service.State);

      kernel.Dispose();
    }

    [Fact]
    public void Constructor_NullKernel_ThrowsArgumentNullException()
    {
      Assert.Throws<ArgumentNullException>(() =>
          new VolumeService(null!, "docker", "my-volume", "local"));
    }

    [Fact]
    public void Constructor_NullDriverId_ThrowsArgumentNullException()
    {
      var kernel = new FluentDockerKernel();
      Assert.Throws<ArgumentNullException>(() =>
          new VolumeService(kernel, null!, "my-volume", "local"));
      kernel.Dispose();
    }

    [Fact]
    public void Constructor_NullVolumeName_ThrowsArgumentNullException()
    {
      var kernel = new FluentDockerKernel();
      Assert.Throws<ArgumentNullException>(() =>
          new VolumeService(kernel, "docker", null!, "local"));
      kernel.Dispose();
    }

    [Fact]
    public void Constructor_NullDriver_DefaultsToLocal()
    {
      var kernel = new FluentDockerKernel();
      var service = new VolumeService(kernel, "docker", "my-volume", null);

      Assert.Equal("local", service.Driver);
      kernel.Dispose();
    }

    [Fact]
    public void Constructor_WithRemoveOnDispose_SetsFlag()
    {
      var kernel = new FluentDockerKernel();
      var service = new VolumeService(kernel, "docker", "my-volume", "local", removeOnDispose: true);

      Assert.NotNull(service);
      kernel.Dispose();
    }

    [Fact]
    public async Task PauseAsync_ThrowsNotSupportedException()
    {
      var kernel = new FluentDockerKernel();
      var service = new VolumeService(kernel, "docker", "my-volume", "local");

      await Assert.ThrowsAsync<NotSupportedException>(async () => await service.PauseAsync());
      kernel.Dispose();
    }

    [Fact]
    public async Task StopAsync_ThrowsNotSupportedException()
    {
      var kernel = new FluentDockerKernel();
      var service = new VolumeService(kernel, "docker", "my-volume", "local");

      await Assert.ThrowsAsync<NotSupportedException>(async () => await service.StopAsync());
      kernel.Dispose();
    }

    [Fact]
    public void AddHook_AddsHook()
    {
      var kernel = new FluentDockerKernel();
      var service = new VolumeService(kernel, "docker", "my-volume", "local");

      service.AddHook(ServiceRunningState.Removed, async _ => { }, "test-hook");
      Assert.NotNull(service);

      kernel.Dispose();
    }

    [Fact]
    public void RemoveHook_RemovesHook()
    {
      var kernel = new FluentDockerKernel();
      var service = new VolumeService(kernel, "docker", "my-volume", "local");
      service.AddHook(ServiceRunningState.Removed, async _ => { }, "test-hook");

      service.RemoveHook("test-hook");
      Assert.NotNull(service);

      kernel.Dispose();
    }
  }
}

