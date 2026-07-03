using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentDocker.Kernel;
using FluentDocker.Services;
using FluentDocker.Services.Impl;
using FluentDocker.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Services
{
  /// <summary>
  /// Tests for IServiceAsync hook registration, removal, firing, and StateChange events.
  /// </summary>
  [Trait("Category", "Unit")]
  public class ServiceHookTests
  {
    private static (ContainerService service, FluentDockerKernel kernel) CreateService()
    {
      var kernel = new FluentDockerKernel(new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      var service = new ContainerService(kernel, "docker", "c1", "nginx", "hook-test");
      return (service, kernel);
    }

    private static async Task<(ContainerService service, FluentDockerKernel kernel)> CreateServiceWithDriverAsync()
    {
      var mockPack = new MockDriverPack();
      mockPack.SetupContainerStart();
      mockPack.SetupContainerStop();
      mockPack.SetupContainerPause();
      mockPack.SetupContainerRemove();
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      var service = new ContainerService(kernel, "docker", "c1", "nginx", "hook-test");
      return (service, kernel);
    }

    [Fact]
    public async Task AddHook_RegistersHookForSpecifiedState()
    {
      var (service, kernel) = await CreateServiceWithDriverAsync();
      try
      {
        var hookFired = false;
        service.AddHook(ServiceRunningState.Running, _ =>
        {
          hookFired = true;
          return Task.CompletedTask;
        }, "my-hook");

        await service.StartAsync(TestContext.Current.CancellationToken);

        Assert.True(hookFired);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task RemoveHook_RemovesRegisteredHook()
    {
      var (service, kernel) = await CreateServiceWithDriverAsync();
      try
      {
        var hookFired = false;
        service.AddHook(ServiceRunningState.Running, _ =>
        {
          hookFired = true;
          return Task.CompletedTask;
        }, "removable");
        service.RemoveHook("removable");

        await service.StartAsync(TestContext.Current.CancellationToken);

        Assert.False(hookFired);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task AddHookWithGeneratedName_ReturnsGuidKeyThatCanBeRemoved()
    {
      var (service, kernel) = await CreateServiceWithDriverAsync();
      try
      {
        var hookFired = false;
        var name = service.AddHookWithGeneratedName(ServiceRunningState.Running, _ =>
        {
          hookFired = true;
          return Task.CompletedTask;
        });
        Assert.True(Guid.TryParse(name, out _), $"Expected GUID key, got: {name}");

        service.RemoveHook(name);
        await service.StartAsync(TestContext.Current.CancellationToken);

        Assert.False(hookFired);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public void AddHook_ReturnsSameServiceInstance_ForChaining()
    {
      var (service, kernel) = CreateService();
      try
      {
        var result = service.AddHook(
            ServiceRunningState.Running, _ => Task.CompletedTask, "fluent");
        Assert.Same(service, result);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public void RemoveHook_NonExistentName_DoesNotThrow()
    {
      var (service, kernel) = CreateService();
      try
      {
        var exception = Record.Exception(() => service.RemoveHook("ghost"));
        Assert.Null(exception);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task StartAsync_FiresStateChangeEvent()
    {
      var (service, kernel) = await CreateServiceWithDriverAsync();
      try
      {
        var eventFired = false;
        service.StateChange += (_, _) => eventFired = true;

        await service.StartAsync(TestContext.Current.CancellationToken);

        Assert.True(eventFired, "StateChange event should fire during StartAsync");
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task StartAsync_EventArgsContainCorrectServiceAndState()
    {
      var (service, kernel) = await CreateServiceWithDriverAsync();
      try
      {
        IServiceAsync? capturedService = null;
        ServiceRunningState? capturedState = null;
        service.StateChange += (_, args) =>
        {
          capturedService = args.Service;
          capturedState = args.State;
        };

        await service.StartAsync(TestContext.Current.CancellationToken);

        Assert.Same(service, capturedService);
        Assert.Equal(ServiceRunningState.Running, capturedState);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task LifecycleOperations_ReportExpectedStatesInArgs()
    {
      var (service, kernel) = await CreateServiceWithDriverAsync();
      try
      {
        var states = new List<ServiceRunningState>();
        service.StateChange += (_, args) => states.Add(args.State);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);
        await service.RemoveAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains(ServiceRunningState.Starting, states);
        Assert.Contains(ServiceRunningState.Running, states);
        Assert.Contains(ServiceRunningState.Stopping, states);
        Assert.Contains(ServiceRunningState.Stopped, states);
        Assert.Contains(ServiceRunningState.Removing, states);
        Assert.Contains(ServiceRunningState.Removed, states);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task RegisteredHook_FiresForMatchingState()
    {
      var (service, kernel) = await CreateServiceWithDriverAsync();
      try
      {
        var hookFired = false;
        service.AddHook(ServiceRunningState.Running, _ =>
        {
          hookFired = true;
          return Task.CompletedTask;
        }, "exec-test");

        await service.StartAsync(TestContext.Current.CancellationToken);

        Assert.True(hookFired);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task RegisteredHook_DoesNotFireForDifferentState()
    {
      var (service, kernel) = await CreateServiceWithDriverAsync();
      try
      {
        var hookFired = false;
        service.AddHook(ServiceRunningState.Stopped, _ =>
        {
          hookFired = true;
          return Task.CompletedTask;
        }, "wrong-state");

        await service.StartAsync(TestContext.Current.CancellationToken);

        Assert.False(hookFired, "Hook for Stopped should not fire during StartAsync");
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task MultipleHooks_SameState_AllFire()
    {
      var (service, kernel) = await CreateServiceWithDriverAsync();
      try
      {
        var firedNames = new List<string>();
        service.AddHook(ServiceRunningState.Starting, _ =>
        {
          firedNames.Add("first");
          return Task.CompletedTask;
        }, "first");
        service.AddHook(ServiceRunningState.Starting, _ =>
        {
          firedNames.Add("second");
          return Task.CompletedTask;
        }, "second");
        service.AddHook(ServiceRunningState.Starting, _ =>
        {
          firedNames.Add("third");
          return Task.CompletedTask;
        }, "third");

        await service.StartAsync(TestContext.Current.CancellationToken);

        Assert.Equal(3, firedNames.Count);
        Assert.Contains("first", firedNames);
        Assert.Contains("second", firedNames);
        Assert.Contains("third", firedNames);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task HookMutatesHooksWhileFiring_DoesNotThrow_AndOthersFire()
    {
      var (service, kernel) = await CreateServiceWithDriverAsync();
      try
      {
        var fired = new List<string>();
        service.AddHook(ServiceRunningState.Starting, _ =>
        {
          fired.Add("self-removing");
          service.RemoveHook("self-removing");
          return Task.CompletedTask;
        }, "self-removing");
        service.AddHook(ServiceRunningState.Starting, _ =>
        {
          fired.Add("survivor");
          return Task.CompletedTask;
        }, "survivor");

        await service.StartAsync(TestContext.Current.CancellationToken);

        Assert.Contains("self-removing", fired);
        Assert.Contains("survivor", fired);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task MultipleHooks_SameState_AllFire_ViaStartAsync()
    {
      var (service, kernel) = await CreateServiceWithDriverAsync();
      try
      {
        var firedNames = new List<string>();
        service.AddHook(ServiceRunningState.Running, _ =>
        {
          firedNames.Add("alpha");
          return Task.CompletedTask;
        }, "alpha");
        service.AddHook(ServiceRunningState.Running, _ =>
        {
          firedNames.Add("beta");
          return Task.CompletedTask;
        }, "beta");

        await service.StartAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, firedNames.Count);
        Assert.Contains("alpha", firedNames);
        Assert.Contains("beta", firedNames);
      }
      finally { kernel.Dispose(); }
    }

#pragma warning disable CA1859 // Intent: verify IServiceAsync interface contract via interface reference
    [Fact]
    public void IServiceAsync_AddHook_ReturnsSameInstance()
    {
      var (service, kernel) = CreateService();
      try
      {
        IServiceAsync asyncService = service;
        var result = asyncService.AddHook(
            ServiceRunningState.Running, _ => Task.CompletedTask, "chain");
        Assert.Same(service, result);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public void IServiceAsync_RemoveHook_ReturnsSameInstance()
    {
      var (service, kernel) = CreateService();
      try
      {
        IServiceAsync asyncService = service;
        asyncService.AddHook(
            ServiceRunningState.Running, _ => Task.CompletedTask, "remove-me");
        var result = asyncService.RemoveHook("remove-me");
        Assert.Same(service, result);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task IServiceAsync_AddHook_FiresThroughPublicLifecycle()
    {
      var (service, kernel) = await CreateServiceWithDriverAsync();
      try
      {
        var fired = false;
        IServiceAsync asyncService = service;
        asyncService.AddHook(ServiceRunningState.Running, _ =>
        {
          fired = true;
          return Task.CompletedTask;
        }, "stored");

        await service.StartAsync(TestContext.Current.CancellationToken);

        Assert.True(fired);
      }
      finally { kernel.Dispose(); }
    }
#pragma warning restore CA1859

    [Fact]
    public async Task StartAsync_ChangesStateProperty()
    {
      var (service, kernel) = await CreateServiceWithDriverAsync();
      try
      {
        Assert.Equal(ServiceRunningState.Unknown, service.State);
        await service.StartAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ServiceRunningState.Running, service.State);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task HookExceptionIsSuppressed()
    {
      var (service, kernel) = await CreateServiceWithDriverAsync();
      try
      {
        service.AddHook(ServiceRunningState.Running, _ =>
            throw new InvalidOperationException("boom"), "throws");

        var exception = await Record.ExceptionAsync(
            () => service.StartAsync(TestContext.Current.CancellationToken));
        Assert.Null(exception);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task StateChange_And_Hooks_BothFireOnStartAsync()
    {
      var (service, kernel) = await CreateServiceWithDriverAsync();
      try
      {
        var stateChanges = new List<ServiceRunningState>();
        var hookStates = new List<ServiceRunningState>();
        service.StateChange += (_, args) => stateChanges.Add(args.State);
        service.AddHook(ServiceRunningState.Starting, _ =>
        {
          hookStates.Add(ServiceRunningState.Starting);
          return Task.CompletedTask;
        }, "starting-hook");
        service.AddHook(ServiceRunningState.Running, _ =>
        {
          hookStates.Add(ServiceRunningState.Running);
          return Task.CompletedTask;
        }, "running-hook");

        await service.StartAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains(ServiceRunningState.Starting, stateChanges);
        Assert.Contains(ServiceRunningState.Running, stateChanges);
        Assert.Contains(ServiceRunningState.Starting, hookStates);
        Assert.Contains(ServiceRunningState.Running, hookStates);
      }
      finally { kernel.Dispose(); }
    }
  }
}
