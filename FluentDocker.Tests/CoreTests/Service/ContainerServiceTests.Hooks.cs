using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentDocker.Kernel;
using FluentDocker.Services;
using FluentDocker.Services.Impl;
using FluentDocker.Tests.Mocks;
using Xunit;

#pragma warning disable CS0618 // IService obsolete — intentional test usage

namespace FluentDocker.Tests.CoreTests.Service
{
  /// <summary>
  /// Tests for IService/IServiceAsync hook registration, removal, firing, and StateChange events.
  /// </summary>
  [Trait("Category", "Unit")]
  public partial class ContainerServiceTests
  {
    #region Hook Firing Tests

    [Fact]
    public async Task StartAsync_FiresRunningHook()
    {
      // Arrange
      var mockPack = new MockDriverPack();
      mockPack.SetupContainerStart();

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      var service = new ContainerService(kernel, "docker", "c1", "nginx", "test");
      var hookFired = false;

      service.AddHook(ServiceRunningState.Running, _ =>
      {
        hookFired = true;
        return Task.CompletedTask;
      }, "run-hook");

      try
      {
        // Act
        await service.StartAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(hookFired, "Running hook should have fired on StartAsync");
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    public async Task StopAsync_FiresStoppedHook()
    {
      // Arrange
      var mockPack = new MockDriverPack();
      mockPack.SetupContainerStop();

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      var service = new ContainerService(kernel, "docker", "c1", "nginx", "test");
      var hookFired = false;

      service.AddHook(ServiceRunningState.Stopped, _ =>
      {
        hookFired = true;
        return Task.CompletedTask;
      }, "stop-hook");

      try
      {
        // Act
        await service.StopAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(hookFired, "Stopped hook should have fired on StopAsync");
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    public async Task PauseAsync_FiresPausedHook()
    {
      // Arrange
      var mockPack = new MockDriverPack();
      mockPack.SetupContainerPause();

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      var service = new ContainerService(kernel, "docker", "c1", "nginx", "test");
      var hookFired = false;

      service.AddHook(ServiceRunningState.Paused, _ =>
      {
        hookFired = true;
        return Task.CompletedTask;
      }, "pause-hook");

      try
      {
        // Act
        await service.PauseAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(hookFired, "Paused hook should have fired on PauseAsync");
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    public async Task RemoveAsync_FiresRemovedHook()
    {
      // Arrange
      var mockPack = new MockDriverPack();
      mockPack.SetupContainerRemove();

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      var service = new ContainerService(kernel, "docker", "c1", "nginx", "test");
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
        Assert.True(hookFired, "Removed hook should have fired on RemoveAsync");
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    public async Task MultipleHooks_SameState_AllFire()
    {
      // Arrange
      var mockPack = new MockDriverPack();
      mockPack.SetupContainerStart();

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      var service = new ContainerService(kernel, "docker", "c1", "nginx", "test");
      var firedHooks = new List<string>();

      service.AddHook(ServiceRunningState.Running, _ =>
      {
        firedHooks.Add("hook-1");
        return Task.CompletedTask;
      }, "hook-1");

      service.AddHook(ServiceRunningState.Running, _ =>
      {
        firedHooks.Add("hook-2");
        return Task.CompletedTask;
      }, "hook-2");

      try
      {
        // Act
        await service.StartAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, firedHooks.Count);
        Assert.Contains("hook-1", firedHooks);
        Assert.Contains("hook-2", firedHooks);
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    public async Task RemoveHook_PreventsHookFromFiring()
    {
      // Arrange
      var mockPack = new MockDriverPack();
      mockPack.SetupContainerStart();

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      var service = new ContainerService(kernel, "docker", "c1", "nginx", "test");
      var hookFired = false;

      service.AddHook(ServiceRunningState.Running, _ =>
      {
        hookFired = true;
        return Task.CompletedTask;
      }, "removable-hook");

      service.RemoveHook("removable-hook");

      try
      {
        // Act
        await service.StartAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.False(hookFired, "Removed hook should not fire");
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    public void RemoveHook_NonExistentName_DoesNotThrow()
    {
      // Arrange
      var kernel = new FluentDockerKernel();
      var service = new ContainerService(kernel, "docker", "c1", "nginx", "test");

      // Act & Assert — should not throw
      service.RemoveHook("does-not-exist");

      kernel.Dispose();
    }

    [Fact]
    public void AddHook_NullName_GeneratesUniqueName()
    {
      // Arrange
      var kernel = new FluentDockerKernel();
      var service = new ContainerService(kernel, "docker", "c1", "nginx", "test");

      // Act — null uniqueName should auto-generate a GUID name
      var result = service.AddHook(ServiceRunningState.Running, _ => Task.CompletedTask);

      // Assert — returns the service for fluent chaining
      Assert.Same(service, result);

      kernel.Dispose();
    }

    #endregion

    #region StateChange Event Tests

    [Fact]
    public async Task StartAsync_RaisesStateChangeEvent()
    {
      // Arrange
      var mockPack = new MockDriverPack();
      mockPack.SetupContainerStart();

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      var service = new ContainerService(kernel, "docker", "c1", "nginx", "test");
      var stateChanges = new List<ServiceRunningState>();

      service.StateChange += (_, args) => stateChanges.Add(args.State);

      try
      {
        // Act
        await service.StartAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(ServiceRunningState.Starting, stateChanges);
        Assert.Contains(ServiceRunningState.Running, stateChanges);
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    public async Task StopAsync_RaisesStateChangeEvent()
    {
      // Arrange
      var mockPack = new MockDriverPack();
      mockPack.SetupContainerStop();

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      var service = new ContainerService(kernel, "docker", "c1", "nginx", "test");
      var stateChanges = new List<ServiceRunningState>();

      service.StateChange += (_, args) => stateChanges.Add(args.State);

      try
      {
        // Act
        await service.StopAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(ServiceRunningState.Stopping, stateChanges);
        Assert.Contains(ServiceRunningState.Stopped, stateChanges);
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    public async Task StateChangeEvent_ContainsCorrectService()
    {
      // Arrange
      var mockPack = new MockDriverPack();
      mockPack.SetupContainerStart();

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      var service = new ContainerService(kernel, "docker", "c1", "nginx", "test");
      IService capturedService = null;

      service.StateChange += (_, args) =>
      {
        capturedService = args.Service;
      };

      try
      {
        // Act
        await service.StartAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(service, capturedService);
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    public async Task IService_AddHook_FluentChaining()
    {
      // Arrange
      var kernel = new FluentDockerKernel();
      var service = new ContainerService(kernel, "docker", "c1", "nginx", "test");
      IService iservice = service;

      // Act — IService.AddHook should return IService for chaining
      var result = iservice.AddHook(ServiceRunningState.Running, _ => { }, "chain-test");

      // Assert
      Assert.Same(service, result);

      kernel.Dispose();
    }

    [Fact]
    public async Task IService_RemoveHook_FluentChaining()
    {
      // Arrange
      var kernel = new FluentDockerKernel();
      var service = new ContainerService(kernel, "docker", "c1", "nginx", "test");
      IService iservice = service;
      iservice.AddHook(ServiceRunningState.Running, _ => { }, "chain-test");

      // Act
      var result = iservice.RemoveHook("chain-test");

      // Assert
      Assert.Same(service, result);

      kernel.Dispose();
    }

    #endregion
  }
}
