using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using FluentDocker.Kernel;
using FluentDocker.Services;
using FluentDocker.Services.Impl;
using FluentDocker.Tests.Mocks;
using Xunit;


namespace FluentDocker.Tests.CoreTests.Services
{
  /// <summary>
  /// Tests for IServiceAsync hook registration, removal, firing,
  /// StateChange event system, and reflection-based private method invocation.
  /// </summary>
  [Trait("Category", "Unit")]
  public class ServiceHookTests
  {
    private static (ContainerService service, FluentDockerKernel kernel) CreateService()
    {
      var kernel = new FluentDockerKernel();
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

    private static void InvokeUpdateState(ContainerService service, ServiceRunningState newState)
    {
      var method = typeof(ContainerService).GetMethod(
          "UpdateState", BindingFlags.NonPublic | BindingFlags.Instance);
      Assert.NotNull(method);
      method.Invoke(service, new object[] { newState });
    }

    private static async Task InvokeExecuteHooksAsync(
        ContainerService service, ServiceRunningState state)
    {
      var method = typeof(ContainerService).GetMethod(
          "ExecuteHooksAsync", BindingFlags.NonPublic | BindingFlags.Instance);
      Assert.NotNull(method);
      await (Task)method.Invoke(service, new object[] { state });
    }

    private static Dictionary<string, Func<IServiceAsync, Task>> GetHooksDictionary(
        ContainerService service)
    {
      var field = typeof(ContainerService).GetField(
          "_hooks", BindingFlags.NonPublic | BindingFlags.Instance);
      Assert.NotNull(field);
      return (Dictionary<string, Func<IServiceAsync, Task>>)field.GetValue(service);
    }

    private static Dictionary<ServiceRunningState, List<Func<IServiceAsync, Task>>>
        GetStateHooksDictionary(ContainerService service)
    {
      var field = typeof(ContainerService).GetField(
          "_stateHooks", BindingFlags.NonPublic | BindingFlags.Instance);
      Assert.NotNull(field);
      return (Dictionary<ServiceRunningState, List<Func<IServiceAsync, Task>>>)field.GetValue(service);
    }

    // 1. AddHook — registering a hook for a specific state stores it
    [Fact]
    public void AddHook_RegistersHookForSpecifiedState()
    {
      var (service, kernel) = CreateService();
      try
      {
        service.AddHook(ServiceRunningState.Running, _ => Task.CompletedTask, "my-hook");

        var hooks = GetHooksDictionary(service);
        Assert.True(hooks.ContainsKey("my-hook"));

        var stateHooks = GetStateHooksDictionary(service);
        Assert.Single(stateHooks[ServiceRunningState.Running]);
      }
      finally { kernel.Dispose(); }
    }

    // 2. RemoveHook — removes from hook dict and all state lists
    [Fact]
    public void RemoveHook_RemovesFromHookDictionaryAndAllStateLists()
    {
      var (service, kernel) = CreateService();
      try
      {
        service.AddHook(ServiceRunningState.Running, _ => Task.CompletedTask, "removable");
        service.RemoveHook("removable");

        var hooks = GetHooksDictionary(service);
        Assert.False(hooks.ContainsKey("removable"));

        var stateHooks = GetStateHooksDictionary(service);
        Assert.Empty(stateHooks[ServiceRunningState.Running]);
      }
      finally { kernel.Dispose(); }
    }

    // 3. AddHook with null uniqueName — auto-generates GUID
    [Fact]
    public void AddHook_NullUniqueName_GeneratesGuidKey()
    {
      var (service, kernel) = CreateService();
      try
      {
        service.AddHook(ServiceRunningState.Stopped, _ => Task.CompletedTask);

        var hooks = GetHooksDictionary(service);
        Assert.Single(hooks);
        foreach (var key in hooks.Keys)
          Assert.True(Guid.TryParse(key, out _), $"Expected GUID key, got: {key}");
      }
      finally { kernel.Dispose(); }
    }

    // 4. AddHook fluent — returns same service instance for chaining
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

    // 5. RemoveHook for non-existent name — does not throw
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

    // 6. StateChange event — fires when UpdateState is called via reflection
    [Fact]
    public void UpdateState_ViaReflection_FiresStateChangeEvent()
    {
      var (service, kernel) = CreateService();
      try
      {
        var eventFired = false;
        service.StateChange += (_, _) => eventFired = true;

        InvokeUpdateState(service, ServiceRunningState.Running);

        Assert.True(eventFired, "StateChange event should fire when UpdateState is called");
      }
      finally { kernel.Dispose(); }
    }

    // 7. StateChange event args — correct service and new state
    [Fact]
    public void UpdateState_ViaReflection_EventArgsContainCorrectServiceAndState()
    {
      var (service, kernel) = CreateService();
      try
      {
        IServiceAsync capturedService = null;
        ServiceRunningState? capturedState = null;
        service.StateChange += (_, args) =>
        {
          capturedService = args.Service;
          capturedState = args.State;
        };

        InvokeUpdateState(service, ServiceRunningState.Paused);

        Assert.Same(service, capturedService);
        Assert.Equal(ServiceRunningState.Paused, capturedState);
      }
      finally { kernel.Dispose(); }
    }

    [Theory]
    [InlineData(ServiceRunningState.Starting)]
    [InlineData(ServiceRunningState.Running)]
    [InlineData(ServiceRunningState.Stopping)]
    [InlineData(ServiceRunningState.Stopped)]
    [InlineData(ServiceRunningState.Removing)]
    [InlineData(ServiceRunningState.Removed)]
    public void UpdateState_ViaReflection_CorrectStateInArgs(ServiceRunningState expected)
    {
      var (service, kernel) = CreateService();
      try
      {
        ServiceRunningState? captured = null;
        service.StateChange += (_, args) => captured = args.State;
        InvokeUpdateState(service, expected);
        Assert.Equal(expected, captured);
      }
      finally { kernel.Dispose(); }
    }

    // 8. Hook execution — ExecuteHooksAsync fires registered hooks
    [Fact]
    public async Task ExecuteHooksAsync_ViaReflection_FiresRegisteredHook()
    {
      var (service, kernel) = CreateService();
      try
      {
        var hookFired = false;
        service.AddHook(ServiceRunningState.Running, _ =>
        {
          hookFired = true;
          return Task.CompletedTask;
        }, "exec-test");

        await InvokeExecuteHooksAsync(service, ServiceRunningState.Running);
        Assert.True(hookFired);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task ExecuteHooksAsync_ViaReflection_DoesNotFireHooksForDifferentState()
    {
      var (service, kernel) = CreateService();
      try
      {
        var hookFired = false;
        service.AddHook(ServiceRunningState.Stopped, _ =>
        {
          hookFired = true;
          return Task.CompletedTask;
        }, "wrong-state");

        await InvokeExecuteHooksAsync(service, ServiceRunningState.Running);
        Assert.False(hookFired, "Hook for Stopped should not fire when Running hooks execute");
      }
      finally { kernel.Dispose(); }
    }

    // 9. Multiple hooks — all execute for the same state
    [Fact]
    public async Task ExecuteHooksAsync_ViaReflection_MultipleHooks_AllFire()
    {
      var (service, kernel) = CreateService();
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

        await InvokeExecuteHooksAsync(service, ServiceRunningState.Starting);

        Assert.Equal(3, firedNames.Count);
        Assert.Contains("first", firedNames);
        Assert.Contains("second", firedNames);
        Assert.Contains("third", firedNames);
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
    // 10. IServiceAsync AddHook — returns same instance for chaining
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
    public void IServiceAsync_AddHook_IsStoredInHooksDictionary()
    {
      var (service, kernel) = CreateService();
      try
      {
        IServiceAsync asyncService = service;
        asyncService.AddHook(
            ServiceRunningState.Stopped, _ => Task.CompletedTask, "stored");

        var hooks = GetHooksDictionary(service);
        Assert.True(hooks.ContainsKey("stored"));

        var stateHooks = GetStateHooksDictionary(service);
        Assert.Single(stateHooks[ServiceRunningState.Stopped]);
      }
      finally { kernel.Dispose(); }
    }
#pragma warning restore CA1859

    // Edge cases
    [Fact]
    public void UpdateState_ViaReflection_ChangesStateProperty()
    {
      var (service, kernel) = CreateService();
      try
      {
        Assert.Equal(ServiceRunningState.Unknown, service.State);
        InvokeUpdateState(service, ServiceRunningState.Running);
        Assert.Equal(ServiceRunningState.Running, service.State);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task ExecuteHooksAsync_ViaReflection_HookExceptionIsSuppressed()
    {
      var (service, kernel) = CreateService();
      try
      {
        service.AddHook(ServiceRunningState.Running, _ =>
            throw new InvalidOperationException("boom"), "throws");

        var exception = await Record.ExceptionAsync(
            () => InvokeExecuteHooksAsync(service, ServiceRunningState.Running));
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
