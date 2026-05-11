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
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Service
{
  /// <summary>
  /// Tests for ComposeService lifecycle: StateChange events, hook execution,
  /// dispose behavior, and full lifecycle transitions.
  /// </summary>
  [Trait("Category", "Unit")]
  public class ComposeServiceLifecycleTests
  {
    private static ComposeService CreateService(
        FluentDockerKernel kernel,
        bool removeVolumes = false,
        bool removeImages = false)
    {
      return new ComposeService(
          kernel, "docker",
          ["docker-compose.yml"],
          "test-project",
          removeVolumes: removeVolumes,
          removeImages: removeImages);
    }

    #region StateChange Event

    [Fact]
    public async Task StartAsync_RaisesStateChangeEvent()
    {
      var mockPack = new MockDriverPack();
      mockPack.SetupComposeStart();

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = CreateService(kernel);
        ServiceRunningState? captured = null;
        service.StateChange += (_, args) => captured = args.State;

        await service.StartAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(captured);
        Assert.Equal(ServiceRunningState.Running, captured.Value);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task StopAsync_RaisesStateChangeEvent()
    {
      var mockPack = new MockDriverPack();
      mockPack.SetupComposeStop();

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = CreateService(kernel);
        ServiceRunningState? captured = null;
        service.StateChange += (_, args) => captured = args.State;

        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(captured);
        Assert.Equal(ServiceRunningState.Stopped, captured.Value);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task PauseAsync_RaisesStateChangeEvent()
    {
      var mockPack = new MockDriverPack();
      mockPack.SetupComposePause();

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = CreateService(kernel);
        ServiceRunningState? captured = null;
        service.StateChange += (_, args) => captured = args.State;

        await service.PauseAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(captured);
        Assert.Equal(ServiceRunningState.Paused, captured.Value);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task RemoveAsync_RaisesStateChangeEvent()
    {
      var mockPack = new MockDriverPack();
      mockPack.SetupComposeDown();

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = CreateService(kernel);
        ServiceRunningState? captured = null;
        service.StateChange += (_, args) => captured = args.State;

        await service.RemoveAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(captured);
        Assert.Equal(ServiceRunningState.Removed, captured.Value);
      }
      finally { kernel.Dispose(); }
    }

    #endregion

    #region Hook Execution

    [Fact]
    public async Task StartAsync_ExecutesRegisteredHooks()
    {
      var mockPack = new MockDriverPack();
      mockPack.SetupComposeStart();

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = CreateService(kernel);
        var hookCalled = false;
        service.AddHook(ServiceRunningState.Running, _ =>
        {
          hookCalled = true;
          return Task.CompletedTask;
        }, "test-start-hook");

        await service.StartAsync(TestContext.Current.CancellationToken);
        Assert.True(hookCalled);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task StopAsync_ExecutesRegisteredHooks()
    {
      var mockPack = new MockDriverPack();
      mockPack.SetupComposeStop();

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = CreateService(kernel);
        var hookCalled = false;
        service.AddHook(ServiceRunningState.Stopped, _ =>
        {
          hookCalled = true;
          return Task.CompletedTask;
        }, "test-stop-hook");

        await service.StopAsync(TestContext.Current.CancellationToken);
        Assert.True(hookCalled);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task Hook_ThrowingException_DoesNotPropagateError()
    {
      var mockPack = new MockDriverPack();
      mockPack.SetupComposeStart();

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = CreateService(kernel);
        service.AddHook(ServiceRunningState.Running, _ =>
            throw new InvalidOperationException("hook boom"), "bad-hook");

        // Should not throw despite the hook exception.
        await service.StartAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ServiceRunningState.Running, service.State);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task AddHook_ReturnsSameInstance()
    {
      var mockPack = new MockDriverPack();
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = CreateService(kernel);
        var result = service.AddHook(
            ServiceRunningState.Running, _ => Task.CompletedTask, "h1");
        Assert.Same(service, result);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task RemoveHook_ReturnsSameInstance()
    {
      var mockPack = new MockDriverPack();
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = CreateService(kernel);
        service.AddHook(
            ServiceRunningState.Running, _ => Task.CompletedTask, "h1");
        var result = service.RemoveHook("h1");
        Assert.Same(service, result);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task RemoveHook_NonExistent_DoesNotThrow()
    {
      var mockPack = new MockDriverPack();
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = CreateService(kernel);
        var result = service.RemoveHook("does-not-exist");
        Assert.Same(service, result);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task AddHook_WithoutName_BothHooksExecute()
    {
      var mockPack = new MockDriverPack();
      mockPack.SetupComposeStart();

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = CreateService(kernel);
        var callCount = 0;
        service.AddHook(ServiceRunningState.Running, _ =>
        {
          callCount++;
          return Task.CompletedTask;
        });
        service.AddHook(ServiceRunningState.Running, _ =>
        {
          callCount++;
          return Task.CompletedTask;
        });

        await service.StartAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, callCount);
      }
      finally { kernel.Dispose(); }
    }

    #endregion

    #region DisposeAsync

    [Fact]
    public async Task DisposeAsync_CallsRemoveWithForce()
    {
      var mockPack = new MockDriverPack();
      mockPack.SetupComposeDown();

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = CreateService(kernel);
        await service.DisposeAsync();

        mockPack.ComposeDriver.Verify(d => d.DownAsync(
            It.IsAny<DriverContext>(),
            It.Is<ComposeDownConfig>(c => c.RemoveVolumes),
            It.IsAny<CancellationToken>()), Times.Once);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task DisposeAsync_DoubleCalls_OnlyRemovesOnce()
    {
      var mockPack = new MockDriverPack();
      mockPack.SetupComposeDown();

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = CreateService(kernel);
        await service.DisposeAsync();
        await service.DisposeAsync();

        mockPack.ComposeDriver.Verify(d => d.DownAsync(
            It.IsAny<DriverContext>(),
            It.IsAny<ComposeDownConfig>(),
            It.IsAny<CancellationToken>()), Times.Once);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task DisposeAsync_DownFailure_DoesNotThrow()
    {
      var mockPack = new MockDriverPack();
      mockPack.ComposeDriver
          .Setup(d => d.DownAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<ComposeDownConfig>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Fail("down failed during dispose"));

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = CreateService(kernel);
        await service.DisposeAsync(); // should not throw
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task Dispose_Sync_CallsDown()
    {
      var mockPack = new MockDriverPack();
      mockPack.SetupComposeDown();

      var kernel = await MockKernelBuilderExtensions
          .CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = CreateService(kernel);
        service.Dispose();

        mockPack.ComposeDriver.Verify(d => d.DownAsync(
            It.IsAny<DriverContext>(),
            It.IsAny<ComposeDownConfig>(),
            It.IsAny<CancellationToken>()), Times.Once);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task Dispose_Sync_DoubleCalls_OnlyRemovesOnce()
    {
      var mockPack = new MockDriverPack();
      mockPack.SetupComposeDown();

      var kernel = await MockKernelBuilderExtensions
          .CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = CreateService(kernel);
        service.Dispose();
        service.Dispose();

        mockPack.ComposeDriver.Verify(d => d.DownAsync(
            It.IsAny<DriverContext>(),
            It.IsAny<ComposeDownConfig>(),
            It.IsAny<CancellationToken>()), Times.Once);
      }
      finally { kernel.Dispose(); }
    }

    #endregion

    #region Full Lifecycle State Transitions

    [Fact]
    public async Task FullLifecycle_TransitionsCorrectly()
    {
      var mockPack = new MockDriverPack();
      mockPack.SetupComposeStart();
      mockPack.SetupComposePause();
      mockPack.SetupComposeStop();
      mockPack.SetupComposeDown();

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = CreateService(kernel);
        var states = new List<ServiceRunningState>();
        service.StateChange += (_, args) => states.Add(args.State);

        Assert.Equal(ServiceRunningState.Running, service.State);

        await service.PauseAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ServiceRunningState.Paused, service.State);

        await service.StartAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ServiceRunningState.Running, service.State);

        await service.StopAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ServiceRunningState.Stopped, service.State);

        await service.RemoveAsync(
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(ServiceRunningState.Removed, service.State);

        Assert.Equal(4, states.Count);
        Assert.Equal(ServiceRunningState.Paused, states[0]);
        Assert.Equal(ServiceRunningState.Running, states[1]);
        Assert.Equal(ServiceRunningState.Stopped, states[2]);
        Assert.Equal(ServiceRunningState.Removed, states[3]);
      }
      finally { kernel.Dispose(); }
    }

    #endregion

    #region IServiceAsync Hook Wrappers

#pragma warning disable CA1859 // Intent: verify IServiceAsync interface contract via interface reference
    [Fact]
    public void IServiceAsync_AddHook_ReturnsSelf()
    {
      var kernel = new FluentDockerKernel(new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      try
      {
        var service = CreateService(kernel);
        IServiceAsync asyncService = service;

        var result = asyncService.AddHook(
            ServiceRunningState.Running, _ => Task.CompletedTask, "hook");

        Assert.Same(service, result);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public void IServiceAsync_RemoveHook_ReturnsSelf()
    {
      var kernel = new FluentDockerKernel(new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      try
      {
        var service = CreateService(kernel);
        IServiceAsync asyncService = service;
        asyncService.AddHook(
            ServiceRunningState.Running, _ => Task.CompletedTask, "hook");

        var result = asyncService.RemoveHook("hook");

        Assert.Same(service, result);
      }
      finally { kernel.Dispose(); }
    }
#pragma warning restore CA1859

    #endregion
  }
}
