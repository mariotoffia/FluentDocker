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
  /// Tests for VolumeService RemoveAsync, DisposeAsync, and unsupported operations.
  /// </summary>
  [Trait("Category", "Unit")]
  public partial class VolumeServiceTests
  {
    #region RemoveAsync Tests

    [Fact]
    public async Task RemoveAsync_Success_UpdatesStateToRemoved()
    {
      // Arrange
      var mockPack = new MockDriverPack();
      mockPack.SetupVolumeRemove();

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      var service = new VolumeService(kernel, "docker", "my-volume", "local");

      try
      {
        Assert.Equal(ServiceRunningState.Running, service.State);

        // Act
        await service.RemoveAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ServiceRunningState.Removed, service.State);
        mockPack.VolumeDriver.Verify(d => d.RemoveAsync(
            It.IsAny<DriverContext>(),
            It.Is<string>(s => s == "my-volume"),
            It.Is<bool>(f => f == false),
            It.IsAny<CancellationToken>()), Times.Once);
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
      mockPack.SetupVolumeRemove();

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      var service = new VolumeService(kernel, "docker", "my-volume", "local");

      try
      {
        // Act
        await service.RemoveAsync(force: true, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        mockPack.VolumeDriver.Verify(d => d.RemoveAsync(
            It.IsAny<DriverContext>(),
            It.Is<string>(s => s == "my-volume"),
            It.Is<bool>(f => f == true),
            It.IsAny<CancellationToken>()), Times.Once);
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
      mockPack.VolumeDriver
          .Setup(d => d.RemoveAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              It.IsAny<bool>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Fail("volume is in use"));

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      var service = new VolumeService(kernel, "docker", "my-volume", "local");

      try
      {
        // Act & Assert
        var ex = await Assert.ThrowsAsync<DriverException>(
            async () => await service.RemoveAsync(cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("volume is in use", ex.Message);
        // State should remain Running on failure
        Assert.Equal(ServiceRunningState.Running, service.State);
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
      mockPack.SetupVolumeRemove();

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      var service = new VolumeService(kernel, "docker", "my-volume", "local");

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
    public async Task RemoveAsync_MultipleHooks_AllFire()
    {
      // Arrange
      var mockPack = new MockDriverPack();
      mockPack.SetupVolumeRemove();

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      var service = new VolumeService(kernel, "docker", "my-volume", "local");

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
      mockPack.SetupVolumeRemove();

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      var service = new VolumeService(kernel, "docker", "my-volume", "local");

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
      mockPack.SetupVolumeRemove();

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      var service = new VolumeService(kernel, "docker", "my-volume", "local");

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

    #region PauseAsync / StopAsync — Unsupported Operations

    [Fact]
    public async Task PauseAsync_MessageContainsCannotBePaused()
    {
      var kernel = new FluentDockerKernel();
      var service = new VolumeService(kernel, "docker", "my-volume", "local");

      var ex = await Assert.ThrowsAsync<NotSupportedException>(
          async () => await service.PauseAsync(TestContext.Current.CancellationToken));

      Assert.Contains("paused", ex.Message, StringComparison.OrdinalIgnoreCase);
      kernel.Dispose();
    }

    [Fact]
    public async Task StopAsync_MessageContainsCannotBeStopped()
    {
      var kernel = new FluentDockerKernel();
      var service = new VolumeService(kernel, "docker", "my-volume", "local");

      var ex = await Assert.ThrowsAsync<NotSupportedException>(
          async () => await service.StopAsync(TestContext.Current.CancellationToken));

      Assert.Contains("stopped", ex.Message, StringComparison.OrdinalIgnoreCase);
      kernel.Dispose();
    }

    #endregion

    #region DisposeAsync Tests

    [Fact]
    public async Task DisposeAsync_WithRemoveOnDispose_CallsRemoveAsync()
    {
      // Arrange
      var mockPack = new MockDriverPack();
      mockPack.SetupVolumeRemove();

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      var service = new VolumeService(kernel, "docker", "my-volume", "local", removeOnDispose: true);

      try
      {
        // Act
        await service.DisposeAsync();

        // Assert — RemoveAsync should have been called with force: true
        Assert.Equal(ServiceRunningState.Removed, service.State);
        mockPack.VolumeDriver.Verify(d => d.RemoveAsync(
            It.IsAny<DriverContext>(),
            It.Is<string>(s => s == "my-volume"),
            It.Is<bool>(f => f == true),
            It.IsAny<CancellationToken>()), Times.Once);
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    public async Task DisposeAsync_WithoutRemoveOnDispose_DoesNotCallRemoveAsync()
    {
      // Arrange
      var mockPack = new MockDriverPack();
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      var service = new VolumeService(kernel, "docker", "my-volume", "local", removeOnDispose: false);

      try
      {
        // Act
        await service.DisposeAsync();

        // Assert — RemoveAsync should NOT have been called
        Assert.Equal(ServiceRunningState.Running, service.State);
        mockPack.VolumeDriver.Verify(d => d.RemoveAsync(
            It.IsAny<DriverContext>(),
            It.IsAny<string>(),
            It.IsAny<bool>(),
            It.IsAny<CancellationToken>()), Times.Never);
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    public async Task DisposeAsync_WithRemoveOnDispose_DriverFailure_DoesNotThrow()
    {
      // Arrange
      var mockPack = new MockDriverPack();
      mockPack.VolumeDriver
          .Setup(d => d.RemoveAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              It.IsAny<bool>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Fail("volume busy"));

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      var service = new VolumeService(kernel, "docker", "my-volume", "local", removeOnDispose: true);

      try
      {
        // Act — should not throw; DisposeCoreAsync catches exceptions
        await service.DisposeAsync();

        // Assert — state stays Running because remove failed
        Assert.Equal(ServiceRunningState.Running, service.State);
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    public async Task DisposeAsync_CalledTwice_OnlyRemovesOnce()
    {
      // Arrange
      var mockPack = new MockDriverPack();
      mockPack.SetupVolumeRemove();

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      var service = new VolumeService(kernel, "docker", "my-volume", "local", removeOnDispose: true);

      try
      {
        // Act
        await service.DisposeAsync();
        await service.DisposeAsync(); // second call should be no-op

        // Assert — only called once due to Interlocked guard
        mockPack.VolumeDriver.Verify(d => d.RemoveAsync(
            It.IsAny<DriverContext>(),
            It.IsAny<string>(),
            It.IsAny<bool>(),
            It.IsAny<CancellationToken>()), Times.Once);
      }
      finally
      {
        kernel.Dispose();
      }
    }

    #endregion
  }
}
