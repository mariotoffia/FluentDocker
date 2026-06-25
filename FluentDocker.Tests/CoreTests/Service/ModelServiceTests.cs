using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Models;
using FluentDocker.Model.Models.Options;
using FluentDocker.Services;
using FluentDocker.Services.Impl;
using FluentDocker.Tests.Mocks;
using Moq;
using Xunit;
using DriverContext = FluentDocker.Model.Drivers.DriverContext;

namespace FluentDocker.Tests.CoreTests.Service
{
  /// <summary>
  /// Unit tests for <see cref="ModelService"/>: state transitions, hooks and
  /// unload-on-dispose, mirroring the container/volume service lifecycle.
  /// </summary>
  [Trait("Category", "Unit")]
  public class ModelServiceTests
  {
    private static readonly ModelReference Model = ModelReference.Parse("ai/smollm2");

    private static async Task<(FluentDocker.Kernel.FluentDockerKernel kernel, ModelService service)> BuildAsync(bool keepRunning = false)
    {
      var pack = new MockDriverPack()
          .SetupModelLoad()
          .SetupModelUnload()
          .SetupModelRemove()
          .SetupModelInspect(new ModelInfo { Reference = Model })
          .EnableModelDrivers();

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack);
      var runner = new ModelRunnerService(kernel, "docker", ModelRunnerEndpoint.HostTcp(), Model);
      var service = new ModelService(kernel, "docker", Model, runner, null, keepRunning);
      return (kernel, service);
    }

    [Fact]
    public async Task Start_TransitionsToRunning()
    {
      var (kernel, service) = await BuildAsync();
      await using (kernel)
      {
        Assert.Equal(ServiceRunningState.Unknown, service.State);
        await service.StartAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ServiceRunningState.Running, service.State);
      }
    }

    [Fact]
    public async Task Stop_TransitionsToStopped()
    {
      var (kernel, service) = await BuildAsync();
      await using (kernel)
      {
        await service.StartAsync(TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ServiceRunningState.Stopped, service.State);
      }
    }

    [Fact]
    public async Task Remove_TransitionsToRemoved()
    {
      var (kernel, service) = await BuildAsync();
      await using (kernel)
      {
        await service.RemoveAsync(false, TestContext.Current.CancellationToken);
        Assert.Equal(ServiceRunningState.Removed, service.State);
      }
    }

    [Fact]
    public async Task Hooks_FireOnStateTransition()
    {
      var (kernel, service) = await BuildAsync();
      await using (kernel)
      {
        var fired = new List<ServiceRunningState>();
        service.AddHook(ServiceRunningState.Running, _ => { fired.Add(ServiceRunningState.Running); return Task.CompletedTask; });

        await service.StartAsync(TestContext.Current.CancellationToken);
        Assert.Contains(ServiceRunningState.Running, fired);
      }
    }

    [Fact]
    public async Task StateChange_EventRaised()
    {
      var (kernel, service) = await BuildAsync();
      await using (kernel)
      {
        var states = new List<ServiceRunningState>();
        service.StateChange += (_, e) => states.Add(e.State);

        await service.StartAsync(TestContext.Current.CancellationToken);

        Assert.Contains(ServiceRunningState.Starting, states);
        Assert.Contains(ServiceRunningState.Running, states);
      }
    }

    [Fact]
    public async Task Pause_NotSupported()
    {
      var (kernel, service) = await BuildAsync();
      await using (kernel)
      {
        await Assert.ThrowsAsync<NotSupportedException>(() => service.PauseAsync(TestContext.Current.CancellationToken));
      }
    }

    [Fact]
    public async Task Dispose_UnloadsWhenNotKeepRunning()
    {
      var pack = new MockDriverPack().SetupModelLoad().SetupModelUnload().EnableModelDrivers();
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack);
      var runner = new ModelRunnerService(kernel, "docker", ModelRunnerEndpoint.HostTcp(), Model);
      var service = new ModelService(kernel, "docker", Model, runner, null, keepRunning: false);

      await service.StartAsync(TestContext.Current.CancellationToken);
      await service.DisposeAsync();

      pack.ModelRuntimeDriver.Verify(d => d.UnloadAsync(
          It.IsAny<DriverContext>(), It.IsAny<ModelReference>(), It.IsAny<System.Threading.CancellationToken>()),
          Times.Once);

      await kernel.DisposeAsync();
    }

    [Fact]
    public async Task Dispose_KeepRunning_DoesNotUnload()
    {
      var pack = new MockDriverPack().SetupModelLoad().SetupModelUnload().EnableModelDrivers();
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack);
      var runner = new ModelRunnerService(kernel, "docker", ModelRunnerEndpoint.HostTcp(), Model);
      var service = new ModelService(kernel, "docker", Model, runner, null, keepRunning: true);

      await service.StartAsync(TestContext.Current.CancellationToken);
      await service.DisposeAsync();

      pack.ModelRuntimeDriver.Verify(d => d.UnloadAsync(
          It.IsAny<DriverContext>(), It.IsAny<ModelReference>(), It.IsAny<System.Threading.CancellationToken>()),
          Times.Never);

      await kernel.DisposeAsync();
    }

    [Fact]
    public async Task SyncDispose_UnloadsWhenNotKeepRunning_WithoutHanging()
    {
      var pack = new MockDriverPack().SetupModelLoad().SetupModelUnload().EnableModelDrivers();
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack);
      var runner = new ModelRunnerService(kernel, "docker", ModelRunnerEndpoint.HostTcp(), Model);
      var service = new ModelService(kernel, "docker", Model, runner, null, keepRunning: false);

      await service.StartAsync(TestContext.Current.CancellationToken);

      // Synchronous Dispose must complete promptly without deadlocking on the
      // thread-pool-dispatched async unload.
      var disposeTask = Task.Run(service.Dispose, TestContext.Current.CancellationToken);
      var completed = await Task.WhenAny(disposeTask, Task.Delay(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken)) == disposeTask;
      Assert.True(completed, "Synchronous Dispose() did not complete in time (possible deadlock).");
      await disposeTask; // surface any exception thrown by Dispose()

      pack.ModelRuntimeDriver.Verify(d => d.UnloadAsync(
          It.IsAny<DriverContext>(), It.IsAny<ModelReference>(), It.IsAny<System.Threading.CancellationToken>()),
          Times.Once);

      await kernel.DisposeAsync();
    }

    [Fact]
    public async Task Dispose_AfterFailedStart_StillAttemptsUnload()
    {
      // NEW9: a load that initiates then FAILS leaves _state == Unknown (never Running),
      // but the model may already be resident — dispose must still best-effort unload it.
      var pack = new MockDriverPack().SetupModelUnload().EnableModelDrivers();
      pack.ModelRuntimeDriver
          .Setup(d => d.LoadAsync(It.IsAny<DriverContext>(), It.IsAny<ModelReference>(), It.IsAny<ModelRunOptions>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Fail("load boom", ErrorCodes.Model.LoadFailed));
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack);
      var runner = new ModelRunnerService(kernel, "docker", ModelRunnerEndpoint.HostTcp(), Model);
      var service = new ModelService(kernel, "docker", Model, runner, null, keepRunning: false);

      await Assert.ThrowsAsync<ModelRunnerException>(() => service.StartAsync(TestContext.Current.CancellationToken));
      await service.DisposeAsync();

      // Best-effort unload was attempted even though Start never reached Running.
      pack.ModelRuntimeDriver.Verify(d => d.UnloadAsync(
          It.IsAny<DriverContext>(), It.IsAny<ModelReference>(), It.IsAny<CancellationToken>()),
          Times.Once);

      await kernel.DisposeAsync();
    }

    [Fact]
    public async Task SyncDispose_KeepRunning_DoesNotUnload()
    {
      var pack = new MockDriverPack().SetupModelLoad().SetupModelUnload().EnableModelDrivers();
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack);
      var runner = new ModelRunnerService(kernel, "docker", ModelRunnerEndpoint.HostTcp(), Model);
      var service = new ModelService(kernel, "docker", Model, runner, null, keepRunning: true);

      await service.StartAsync(TestContext.Current.CancellationToken);
      service.Dispose();

      pack.ModelRuntimeDriver.Verify(d => d.UnloadAsync(
          It.IsAny<DriverContext>(), It.IsAny<ModelReference>(), It.IsAny<System.Threading.CancellationToken>()),
          Times.Never);

      await kernel.DisposeAsync();
    }

    [Fact]
    public async Task SyncDispose_IsIdempotent()
    {
      var pack = new MockDriverPack().SetupModelLoad().SetupModelUnload().EnableModelDrivers();
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack);
      var runner = new ModelRunnerService(kernel, "docker", ModelRunnerEndpoint.HostTcp(), Model);
      var service = new ModelService(kernel, "docker", Model, runner, null, keepRunning: false);

      await service.StartAsync(TestContext.Current.CancellationToken);

      service.Dispose();
      service.Dispose(); // second call must be a no-op (no second unload)

      pack.ModelRuntimeDriver.Verify(d => d.UnloadAsync(
          It.IsAny<DriverContext>(), It.IsAny<ModelReference>(), It.IsAny<System.Threading.CancellationToken>()),
          Times.Once);

      await kernel.DisposeAsync();
    }

    [Fact]
    public async Task Runner_And_Model_Exposed()
    {
      var (kernel, service) = await BuildAsync();
      await using (kernel)
      {
        Assert.Equal(Model, service.Model);
        Assert.NotNull(service.Runner);
      }
    }
  }
}
