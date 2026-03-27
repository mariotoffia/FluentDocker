using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using FluentDocker.Services;
using FluentDocker.Services.Impl;
using FluentDocker.Tests.Mocks;
using Moq;
using Xunit;

#pragma warning disable CS0618 // IService obsolete — intentional test usage

namespace FluentDocker.Tests.CoreTests.Service
{
  /// <summary>
  /// Unit tests for ImageService.
  /// </summary>
  [Trait("Category", "Unit")]
  public class ImageServiceTests
  {
    #region Constructor Tests

    [Fact]
    public void Constructor_SetsProperties()
    {
      // Arrange
      var kernel = new FluentDockerKernel();

      // Act
      var service = new ImageService(kernel, "docker", "sha256:abc123", "nginx", "1.25");

      // Assert
      Assert.Equal("sha256:abc123", service.Id);
      Assert.Equal("1.25", service.Tag);
      Assert.Equal("nginx:1.25", service.FullName);
      Assert.Equal("nginx:1.25", service.Name);
      Assert.Equal(ServiceRunningState.Running, service.State);
      Assert.Equal(kernel, service.Kernel);
      Assert.Equal("docker", service.DriverId);

      kernel.Dispose();
    }

    [Fact]
    public void Constructor_NullKernel_ThrowsArgumentNullException()
    {
      Assert.Throws<ArgumentNullException>(() =>
          new ImageService(null!, "docker", "sha256:abc123", "nginx", "latest"));
    }

    [Fact]
    public void Constructor_NullDriverId_ThrowsArgumentNullException()
    {
      var kernel = new FluentDockerKernel();
      Assert.Throws<ArgumentNullException>(() =>
          new ImageService(kernel, null!, "sha256:abc123", "nginx", "latest"));
      kernel.Dispose();
    }

    [Fact]
    public void Constructor_NullImageId_ThrowsArgumentNullException()
    {
      var kernel = new FluentDockerKernel();
      Assert.Throws<ArgumentNullException>(() =>
          new ImageService(kernel, "docker", null!, "nginx", "latest"));
      kernel.Dispose();
    }

    [Fact]
    public void Constructor_NullTag_DefaultsToLatest()
    {
      var kernel = new FluentDockerKernel();

      var service = new ImageService(kernel, "docker", "sha256:abc123", "nginx", null);

      Assert.Equal("latest", service.Tag);
      kernel.Dispose();
    }

    #endregion

    #region FullName Tests

    [Fact]
    public void FullName_WithRepository_CombinesRepositoryAndTag()
    {
      var kernel = new FluentDockerKernel();

      var service = new ImageService(kernel, "docker", "sha256:abc123", "myregistry/nginx", "2.0");

      Assert.Equal("myregistry/nginx:2.0", service.FullName);
      kernel.Dispose();
    }

    [Fact]
    public void FullName_WithoutRepository_ReturnsImageId()
    {
      var kernel = new FluentDockerKernel();

      var service = new ImageService(kernel, "docker", "sha256:abc123", null, "latest");

      Assert.Equal("sha256:abc123", service.FullName);
      kernel.Dispose();
    }

    #endregion

    #region Operation Tests

    [Fact]
    public async Task InspectAsync_Success_ReturnsImage()
    {
      // Arrange
      var mockPack = new MockDriverPack();
      mockPack.SetupImageInspect("sha256:abc123");

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      var service = new ImageService(kernel, "docker", "sha256:abc123", "nginx", "latest");

      try
      {
        // Act
        var image = await service.InspectAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(image);
        Assert.Equal("sha256:abc123", image.Id);
        mockPack.ImageDriver.Verify(d => d.InspectAsync(
            It.IsAny<DriverContext>(),
            It.Is<string>(s => s == "sha256:abc123"),
            It.IsAny<CancellationToken>()), Times.Once);
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    public async Task InspectAsync_Failure_ThrowsDriverException()
    {
      // Arrange
      var mockPack = new MockDriverPack();
      mockPack.ImageDriver
          .Setup(d => d.InspectAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Image>.Fail("inspect failed"));

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      var service = new ImageService(kernel, "docker", "sha256:abc123", "nginx", "latest");

      try
      {
        // Act & Assert
        var ex = await Assert.ThrowsAsync<DriverException>(
            async () => await service.InspectAsync(TestContext.Current.CancellationToken));

        Assert.Contains("inspect failed", ex.Message);
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    public async Task GetHistoryAsync_Success_ReturnsLayers()
    {
      // Arrange
      var layers = new List<ImageLayer>
      {
        new ImageLayer { Id = "layer1", CreatedBy = "ADD file:abc", Size = 1024 },
        new ImageLayer { Id = "layer2", CreatedBy = "RUN apt-get update", Size = 2048 }
      };

      var mockPack = new MockDriverPack();
      mockPack.SetupImageHistory(layers);

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      var service = new ImageService(kernel, "docker", "sha256:abc123", "nginx", "latest");

      try
      {
        // Act
        var result = await service.GetHistoryAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.Equal("layer1", result[0].Id);
        Assert.Equal("layer2", result[1].Id);
        mockPack.ImageDriver.Verify(d => d.HistoryAsync(
            It.IsAny<DriverContext>(),
            It.Is<string>(s => s == "sha256:abc123"),
            It.IsAny<CancellationToken>()), Times.Once);
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    public async Task TagAsync_Success_DoesNotThrow()
    {
      // Arrange
      var mockPack = new MockDriverPack();
      mockPack.SetupImageTag();

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      var service = new ImageService(kernel, "docker", "sha256:abc123", "nginx", "latest");

      try
      {
        // Act — should not throw
        await service.TagAsync("myregistry/nginx", "v2.0", TestContext.Current.CancellationToken);

        // Assert
        mockPack.ImageDriver.Verify(d => d.TagAsync(
            It.IsAny<DriverContext>(),
            It.Is<string>(s => s == "sha256:abc123"),
            It.Is<string>(s => s == "myregistry/nginx"),
            It.Is<string>(s => s == "v2.0"),
            It.IsAny<CancellationToken>()), Times.Once);
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    public async Task PushAsync_Success_DoesNotThrow()
    {
      // Arrange
      var mockPack = new MockDriverPack();
      mockPack.SetupImagePush();

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      var service = new ImageService(kernel, "docker", "sha256:abc123", "nginx", "latest");

      try
      {
        // Act — should not throw
        await service.PushAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        mockPack.ImageDriver.Verify(d => d.PushAsync(
            It.IsAny<DriverContext>(),
            It.Is<string>(s => s == "nginx:latest"),
            It.IsAny<IProgress<ImagePushProgress>>(),
            It.IsAny<CancellationToken>()), Times.Once);
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    public async Task SaveAsync_Success_DoesNotThrow()
    {
      // Arrange
      var mockPack = new MockDriverPack();
      mockPack.SetupImageSave();

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      var service = new ImageService(kernel, "docker", "sha256:abc123", "nginx", "latest");

      try
      {
        // Act — should not throw
        await service.SaveAsync("/tmp/nginx.tar", TestContext.Current.CancellationToken);

        // Assert
        mockPack.ImageDriver.Verify(d => d.SaveAsync(
            It.IsAny<DriverContext>(),
            It.Is<string[]>(arr => arr.Length == 1 && arr[0] == "nginx:latest"),
            It.Is<string>(s => s == "/tmp/nginx.tar"),
            It.IsAny<CancellationToken>()), Times.Once);
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    public async Task RemoveAsync_Success_UpdatesStateToRemoved()
    {
      // Arrange
      var mockPack = new MockDriverPack();
      mockPack.SetupImageRemove();

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      var service = new ImageService(kernel, "docker", "sha256:abc123", "nginx", "latest");

      try
      {
        // Act
        await service.RemoveAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ServiceRunningState.Removed, service.State);
        mockPack.ImageDriver.Verify(d => d.RemoveAsync(
            It.IsAny<DriverContext>(),
            It.Is<string>(s => s == "sha256:abc123"),
            It.IsAny<bool>(),
            It.IsAny<bool>(),
            It.IsAny<CancellationToken>()), Times.Once);
      }
      finally
      {
        kernel.Dispose();
      }
    }

    #endregion

    #region Unsupported Operation Tests

    [Fact]
    public async Task PauseAsync_ThrowsNotSupportedException()
    {
      var kernel = new FluentDockerKernel();
      var service = new ImageService(kernel, "docker", "sha256:abc123", "nginx", "latest");

      await Assert.ThrowsAsync<NotSupportedException>(
          async () => await service.PauseAsync(TestContext.Current.CancellationToken));

      kernel.Dispose();
    }

    [Fact]
    public async Task StopAsync_ThrowsNotSupportedException()
    {
      var kernel = new FluentDockerKernel();
      var service = new ImageService(kernel, "docker", "sha256:abc123", "nginx", "latest");

      await Assert.ThrowsAsync<NotSupportedException>(
          async () => await service.StopAsync(TestContext.Current.CancellationToken));

      kernel.Dispose();
    }

    [Fact]
    public async Task StartAsync_CompletesSuccessfully()
    {
      var kernel = new FluentDockerKernel();
      var service = new ImageService(kernel, "docker", "sha256:abc123", "nginx", "latest");

      // Act — should complete without throwing
      await service.StartAsync(TestContext.Current.CancellationToken);

      // Assert — state remains Running (no-op)
      Assert.Equal(ServiceRunningState.Running, service.State);
      kernel.Dispose();
    }

    #endregion

    #region Hook Tests

    [Fact]
    public void AddHook_StoresHook_RemoveHook_RemovesHook()
    {
      var kernel = new FluentDockerKernel();
      var service = new ImageService(kernel, "docker", "sha256:abc123", "nginx", "latest");

      // Act — add hook, returns self for fluent chaining
      var result = service.AddHook(ServiceRunningState.Removed, async _ => { }, "test-hook");
      Assert.Same(service, result);

      // Act — remove hook, returns self for fluent chaining
      var result2 = service.RemoveHook("test-hook");
      Assert.Same(service, result2);

      kernel.Dispose();
    }

    #endregion
  }
}
