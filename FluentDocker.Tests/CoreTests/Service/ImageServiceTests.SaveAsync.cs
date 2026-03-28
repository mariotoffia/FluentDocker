using System;
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
  /// Tests for ImageService SaveAsync, RemoveAsync (hooks), and unsupported operations.
  /// </summary>
  [Trait("Category", "Unit")]
  public partial class ImageServiceTests
  {
    #region SaveAsync Tests

    [Fact]
    public async Task SaveAsync_Success_CallsDriverAndCompletes()
    {
      // Arrange
      var mockPack = new MockDriverPack();
      mockPack.SetupImageSave();

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      var service = new ImageService(kernel, "docker", "sha256:def456", "redis", "7.0");

      try
      {
        // Act
        await service.SaveAsync("/tmp/redis.tar", TestContext.Current.CancellationToken);

        // Assert
        mockPack.ImageDriver.Verify(d => d.SaveAsync(
            It.IsAny<DriverContext>(),
            It.Is<string[]>(arr => arr.Length == 1 && arr[0] == "redis:7.0"),
            It.Is<string>(s => s == "/tmp/redis.tar"),
            It.IsAny<CancellationToken>()), Times.Once);
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    public async Task SaveAsync_DriverFailure_ThrowsDriverException()
    {
      // Arrange
      var mockPack = new MockDriverPack();
      mockPack.ImageDriver
          .Setup(d => d.SaveAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string[]>(),
              It.IsAny<string>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Fail("disk full"));

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      var service = new ImageService(kernel, "docker", "sha256:def456", "redis", "7.0");

      try
      {
        // Act & Assert
        var ex = await Assert.ThrowsAsync<DriverException>(
            async () => await service.SaveAsync("/tmp/redis.tar", TestContext.Current.CancellationToken));

        Assert.Contains("disk full", ex.Message);
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    public async Task SaveAsync_WithoutRepository_UsesImageIdAsFullName()
    {
      // Arrange
      var mockPack = new MockDriverPack();
      mockPack.SetupImageSave();

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      // No repository => FullName falls back to imageId
      var service = new ImageService(kernel, "docker", "sha256:norepository", null, "latest");

      try
      {
        // Act
        await service.SaveAsync("/tmp/output.tar", TestContext.Current.CancellationToken);

        // Assert — FullName should be the imageId when repository is null
        mockPack.ImageDriver.Verify(d => d.SaveAsync(
            It.IsAny<DriverContext>(),
            It.Is<string[]>(arr => arr.Length == 1 && arr[0] == "sha256:norepository"),
            It.Is<string>(s => s == "/tmp/output.tar"),
            It.IsAny<CancellationToken>()), Times.Once);
      }
      finally
      {
        kernel.Dispose();
      }
    }

    #endregion

    #region PauseAsync / StopAsync — Unsupported Operations (extended)

    [Fact]
    public async Task PauseAsync_MessageContainsCannotBePaused()
    {
      var kernel = new FluentDockerKernel();
      var service = new ImageService(kernel, "docker", "sha256:abc123", "nginx", "latest");

      var ex = await Assert.ThrowsAsync<NotSupportedException>(
          async () => await service.PauseAsync(TestContext.Current.CancellationToken));

      Assert.Contains("paused", ex.Message, StringComparison.OrdinalIgnoreCase);
      kernel.Dispose();
    }

    [Fact]
    public async Task StopAsync_MessageContainsCannotBeStopped()
    {
      var kernel = new FluentDockerKernel();
      var service = new ImageService(kernel, "docker", "sha256:abc123", "nginx", "latest");

      var ex = await Assert.ThrowsAsync<NotSupportedException>(
          async () => await service.StopAsync(TestContext.Current.CancellationToken));

      Assert.Contains("stopped", ex.Message, StringComparison.OrdinalIgnoreCase);
      kernel.Dispose();
    }

    #endregion

    #region RemoveAsync Hook and State Tests

    [Fact]
    public async Task RemoveAsync_Success_UpdatesStateToRemoved_WithMockSetup()
    {
      // Arrange
      var mockPack = new MockDriverPack();
      mockPack.SetupImageRemove();

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      var service = new ImageService(kernel, "docker", "sha256:abc123", "nginx", "latest");

      try
      {
        Assert.Equal(ServiceRunningState.Running, service.State);

        // Act
        await service.RemoveAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ServiceRunningState.Removed, service.State);
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    public async Task RemoveAsync_FiresHooksAfterRemoval()
    {
      // Arrange
      var mockPack = new MockDriverPack();
      mockPack.SetupImageRemove();

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      var service = new ImageService(kernel, "docker", "sha256:abc123", "nginx", "latest");

      var hookFired = false;
      service.AddHook(ServiceRunningState.Removed, _ =>
      {
        hookFired = true;
        return Task.CompletedTask;
      }, "remove-hook");

      try
      {
        // Act
        await service.RemoveAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(hookFired, "Remove hook should have fired after RemoveAsync");
        Assert.Equal(ServiceRunningState.Removed, service.State);
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    public async Task RemoveAsync_DriverFailure_ThrowsDriverException()
    {
      // Arrange
      var mockPack = new MockDriverPack();
      mockPack.ImageDriver
          .Setup(d => d.RemoveAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              It.IsAny<bool>(),
              It.IsAny<bool>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<ImageRemoveResult>.Fail("image in use"));

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      var service = new ImageService(kernel, "docker", "sha256:abc123", "nginx", "latest");

      try
      {
        // Act & Assert
        var ex = await Assert.ThrowsAsync<DriverException>(
            async () => await service.RemoveAsync(cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("image in use", ex.Message);
        // State should remain Running on failure
        Assert.Equal(ServiceRunningState.Running, service.State);
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    public async Task RemoveAsync_WithForceFlag_PassesForceTrueToDriver()
    {
      // Arrange
      var mockPack = new MockDriverPack();
      mockPack.SetupImageRemove();

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      var service = new ImageService(kernel, "docker", "sha256:abc123", "nginx", "latest");

      try
      {
        // Act
        await service.RemoveAsync(force: true, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        mockPack.ImageDriver.Verify(d => d.RemoveAsync(
            It.IsAny<DriverContext>(),
            It.Is<string>(s => s == "sha256:abc123"),
            It.Is<bool>(f => f == true),
            It.Is<bool>(np => np == false),
            It.IsAny<CancellationToken>()), Times.Once);
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    public async Task RemoveAsync_MultipleHooks_AllFire()
    {
      // Arrange
      var mockPack = new MockDriverPack();
      mockPack.SetupImageRemove();

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      var service = new ImageService(kernel, "docker", "sha256:abc123", "nginx", "latest");

      var hook1Fired = false;
      var hook2Fired = false;

      service.AddHook(ServiceRunningState.Removed, _ =>
      {
        hook1Fired = true;
        return Task.CompletedTask;
      }, "hook-1");

      service.AddHook(ServiceRunningState.Removed, _ =>
      {
        hook2Fired = true;
        return Task.CompletedTask;
      }, "hook-2");

      try
      {
        // Act
        await service.RemoveAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(hook1Fired, "First hook should have fired");
        Assert.True(hook2Fired, "Second hook should have fired");
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    public async Task RemoveAsync_HookThrows_DoesNotPreventOtherHooks()
    {
      // Arrange
      var mockPack = new MockDriverPack();
      mockPack.SetupImageRemove();

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      var service = new ImageService(kernel, "docker", "sha256:abc123", "nginx", "latest");

      var secondHookFired = false;

      service.AddHook(ServiceRunningState.Removed, _ =>
      {
        throw new InvalidOperationException("Hook failure");
      }, "failing-hook");

      service.AddHook(ServiceRunningState.Removed, _ =>
      {
        secondHookFired = true;
        return Task.CompletedTask;
      }, "second-hook");

      try
      {
        // Act — should complete despite hook exception
        await service.RemoveAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(secondHookFired, "Second hook should fire even when first hook throws");
        Assert.Equal(ServiceRunningState.Removed, service.State);
      }
      finally
      {
        kernel.Dispose();
      }
    }

    #endregion

    #region StateChange Event Tests

    [Fact]
    public async Task RemoveAsync_FiresStateChangeEvent()
    {
      // Arrange
      var mockPack = new MockDriverPack();
      mockPack.SetupImageRemove();

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      var service = new ImageService(kernel, "docker", "sha256:abc123", "nginx", "latest");

      ServiceRunningState? capturedState = null;
      service.StateChange += (sender, args) =>
      {
        capturedState = args.State;
      };

      try
      {
        // Act
        await service.RemoveAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(capturedState);
        Assert.Equal(ServiceRunningState.Removed, capturedState.Value);
      }
      finally
      {
        kernel.Dispose();
      }
    }

    #endregion
  }
}
