using FluentDocker.Common;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Models;
using FluentDocker.Model.Models.Options;
using FluentDocker.Testing.Core;
using FluentDocker.Tests.Mocks;
using Moq;
using Xunit;
using DriverContext = FluentDocker.Model.Drivers.DriverContext;

namespace FluentDocker.Tests.CoreTests.Testing
{
  [Trait("Category", "Unit")]
  public class ModelResourceTests
  {
    private static readonly ModelReference Model = ModelReference.Parse("ai/smollm2:latest");

    // A model whose unload is wired to HANG (token-ignoring driver) holds the process-wide
    // ModelOperationGate for the full op — by design (serialization must not release mid-op).
    // Such tests must therefore use a UNIQUE model so the deliberately-orphaned gate cannot
    // poison sibling tests that share a model key. Mirrors ModelOperationGateTests.UniqueKey().
    private static ModelReference UniqueModel() =>
        ModelReference.Parse("ai/hung-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task InitializeAsync_LoadsModel_AndExposesServiceRunnerModel()
    {
      var pack = new MockDriverPack()
          .SetupModelLoad()
          .SetupModelUnload()
          .EnableModelDrivers();
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack);
      await using (kernel)
      {
        var resource = new ModelResource(kernel, Model);

        await resource.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.True(resource.IsInitialized);
        Assert.Same(resource.Service.Runner, resource.Runner);
        Assert.Equal(Model, resource.Model);
        pack.ModelRuntimeDriver.Verify(d => d.LoadAsync(
            It.IsAny<DriverContext>(), It.Is<ModelReference>(m => m.Equals(Model)),
            It.IsAny<ModelRunOptions>(), It.IsAny<CancellationToken>()), Times.Once);

        await resource.DisposeAsync();
      }
    }

    [Fact]
    public async Task Properties_BeforeInitialize_Throw()
    {
      var pack = new MockDriverPack().EnableModelDrivers();
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack);
      await using (kernel)
      {
        var resource = new ModelResource(kernel, Model);

        var serviceError = Record.Exception(() => _ = resource.Service);
        var runnerError = Record.Exception(() => _ = resource.Runner);

        Assert.IsType<InvalidOperationException>(serviceError);
        Assert.IsType<InvalidOperationException>(runnerError);
        // Model is known from construction and must be readable before init.
        Assert.Equal(Model, resource.Model);
      }
    }

    [Fact]
    public async Task DisposeAsync_UnloadsModel_UnlessKeepRunning()
    {
      var unloadPack = new MockDriverPack()
          .SetupModelLoad()
          .SetupModelUnload()
          .EnableModelDrivers();
      var keepPack = new MockDriverPack()
          .SetupModelLoad()
          .SetupModelUnload()
          .EnableModelDrivers();
      var unloadKernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", unloadPack);
      var keepKernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", keepPack);
      await using (unloadKernel)
      await using (keepKernel)
      {
        var unloadResource = new ModelResource(unloadKernel, Model);
        var keepResource = new ModelResource(keepKernel, Model, b => b.KeepRunning());

        await unloadResource.InitializeAsync(TestContext.Current.CancellationToken);
        await unloadResource.DisposeAsync();
        await unloadResource.DisposeAsync();

        await keepResource.InitializeAsync(TestContext.Current.CancellationToken);
        await keepResource.DisposeAsync();

        unloadPack.ModelRuntimeDriver.Verify(d => d.UnloadAsync(
            It.IsAny<DriverContext>(), It.Is<ModelReference>(m => m.Equals(Model)),
            It.IsAny<CancellationToken>()), Times.Once);
        keepPack.ModelRuntimeDriver.Verify(d => d.UnloadAsync(
            It.IsAny<DriverContext>(), It.IsAny<ModelReference>(),
            It.IsAny<CancellationToken>()), Times.Never);
      }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DisposeAsync_HungUnload_ObservesTeardownTimeout()
    {
      var unload = new TaskCompletionSource<CommandResponse<Unit>>();
      var pack = new MockDriverPack()
          .SetupModelLoad()
          .EnableModelDrivers();
      pack.ModelRuntimeDriver
          .Setup(d => d.UnloadAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<ModelReference>(),
              It.IsAny<CancellationToken>()))
          .Returns(unload.Task);
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack);
      await using (kernel)
      {
        var resource = new ModelResource(kernel, UniqueModel(), options: new DockerResourceOptions
        {
          ForceRemoveOnDispose = false,
          TeardownTimeout = TimeSpan.FromMilliseconds(100)
        });

        await resource.InitializeAsync(TestContext.Current.CancellationToken);
        var disposeTask = resource.DisposeAsync().AsTask();
        var completed = await Task.WhenAny(
            disposeTask,
            Task.Delay(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));

        Assert.Same(disposeTask, completed);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => disposeTask);
        Assert.NotNull(resource.LastTeardownDiagnostics);

        // Release the orphaned (timed-out) unload so its gate holder completes and frees the
        // per-model gate instead of leaking it for the process lifetime.
        unload.TrySetResult(CommandResponse<Unit>.Ok(Unit.Default));
      }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DisposeAsync_HungUnload_DefaultForceRemove_DoesNotFakeRecovery()
    {
      // Default ForceRemoveOnDispose=true. The graceful teardown's service.DisposeAsync()
      // sets _disposed=1 and hangs on unload; ForceRemoveAsync MUST go straight to the
      // runtime driver (which also hangs) and FAIL — not reuse the now-no-op DisposeAsync()
      // and fake recovery.
      var unload = new TaskCompletionSource<CommandResponse<Unit>>();
      var pack = new MockDriverPack()
          .SetupModelLoad()
          .EnableModelDrivers();
      pack.ModelRuntimeDriver
          .Setup(d => d.UnloadAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<ModelReference>(),
              It.IsAny<CancellationToken>()))
          .Returns(unload.Task);
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack);
      await using (kernel)
      {
        var resource = new ModelResource(kernel, UniqueModel(), options: new DockerResourceOptions
        {
          ForceRemoveOnDispose = true,
          TeardownTimeout = TimeSpan.FromMilliseconds(100)
        });

        await resource.InitializeAsync(TestContext.Current.CancellationToken);
        var disposeTask = resource.DisposeAsync().AsTask();
        var completed = await Task.WhenAny(
            disposeTask,
            Task.Delay(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));

        Assert.Same(disposeTask, completed);
        // Must NOT silently succeed — both teardown and force-remove hung/failed.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => disposeTask);
        Assert.NotNull(resource.LastTeardownDiagnostics);
        Assert.NotNull(resource.LastTeardownDiagnostics.TeardownException);
        Assert.NotNull(resource.LastTeardownDiagnostics.ForceRemoveException);
        // Force-remove attempted the real driver unload (graceful unload + force unload).
        pack.ModelRuntimeDriver.Verify(d => d.UnloadAsync(
            It.IsAny<DriverContext>(), It.IsAny<ModelReference>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));

        // Release the orphaned (timed-out) unload(s) so the gate holder completes and frees the
        // per-model gate instead of leaking it for the process lifetime.
        unload.TrySetResult(CommandResponse<Unit>.Ok(Unit.Default));
      }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DisposeAsync_FailedUnload_IsSurfaced_NotSwallowed()
    {
      // Item 7: ModelService.DisposeAsync() logs/swallows unload failures, so cleanup could
      // report success while the model is still resident. ModelResource teardown now runs the
      // STRICT StopAsync first, so a FAILED unload surfaces instead of being hidden.
      var pack = new MockDriverPack()
          .SetupModelLoad()
          .EnableModelDrivers();
      pack.ModelRuntimeDriver
          .Setup(d => d.UnloadAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<ModelReference>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Fail("unload failed", ErrorCodes.Model.UnloadFailed));
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack);
      await using (kernel)
      {
        var resource = new ModelResource(kernel, Model, options: new DockerResourceOptions
        {
          ForceRemoveOnDispose = false
        });

        await resource.InitializeAsync(TestContext.Current.CancellationToken);

        // Cleanup must NOT silently succeed when the unload failed.
        await Assert.ThrowsAsync<ModelRunnerException>(() => resource.DisposeAsync().AsTask());
        Assert.NotNull(resource.LastTeardownDiagnostics);
        Assert.NotNull(resource.LastTeardownDiagnostics.TeardownException);
        // The strict StopAsync attempted the unload (and surfaced its failure).
        pack.ModelRuntimeDriver.Verify(d => d.UnloadAsync(
            It.IsAny<DriverContext>(), It.IsAny<ModelReference>(),
            It.IsAny<CancellationToken>()), Times.Once);
      }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DisposeAsync_FailedStop_RetryStaysStrict_NotDowngradedToSwallow()
    {
      // A2: a failed strict StopAsync moves state to Unknown. The teardown guard must keep
      // strict-stopping for ANY non-terminal state (not only Running), so the NEXT DisposeAsync
      // RETRIES the strict stop and surfaces the still-failing unload — instead of falling through
      // to the swallowing service.DisposeAsync() and faking success with a still-resident model.
      var pack = new MockDriverPack()
          .SetupModelLoad()
          .EnableModelDrivers();
      pack.ModelRuntimeDriver
          .Setup(d => d.UnloadAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<ModelReference>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Fail("unload failed", ErrorCodes.Model.UnloadFailed));
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack);
      await using (kernel)
      {
        var resource = new ModelResource(kernel, Model, options: new DockerResourceOptions
        {
          ForceRemoveOnDispose = false
        });

        await resource.InitializeAsync(TestContext.Current.CancellationToken);

        // First cleanup surfaces the failure (state -> Unknown) and keeps the resource provisioned.
        await Assert.ThrowsAsync<ModelRunnerException>(() => resource.DisposeAsync().AsTask());
        // The RETRY must STILL be strict and surface the failure — the old Running-only guard
        // would skip strict-stop here and swallow it via service.DisposeAsync().
        await Assert.ThrowsAsync<ModelRunnerException>(() => resource.DisposeAsync().AsTask());

        pack.ModelRuntimeDriver.Verify(d => d.UnloadAsync(
            It.IsAny<DriverContext>(), It.IsAny<ModelReference>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
      }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ForceRemove_DisposesOwnedRunner_AfterConfirmedUnload()
    {
      // A3: when graceful teardown fails and force-remove succeeds, the owned runner (inference
      // connection / HttpClient / X509 cert) must be DISPOSED — not leaked by merely clearing the
      // service reference.
      var pack = new MockDriverPack()
          .SetupModelLoad()
          .EnableModelDrivers();
      pack.ModelRuntimeDriver
          .SetupSequence(d => d.UnloadAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<ModelReference>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Fail("graceful unload failed", ErrorCodes.Model.UnloadFailed))
          .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack);
      await using (kernel)
      {
        var resource = new ModelResource(kernel, Model, options: new DockerResourceOptions
        {
          ForceRemoveOnDispose = true
        });

        await resource.InitializeAsync(TestContext.Current.CancellationToken);
        var runner = resource.Runner; // capture before teardown nulls the service

        // Graceful strict-stop fails (#1); force-remove unload succeeds (#2) -> recovered, no throw.
        await resource.DisposeAsync();

        Assert.NotNull(resource.LastTeardownDiagnostics);
        Assert.NotNull(resource.LastTeardownDiagnostics.TeardownException);
        Assert.Null(resource.LastTeardownDiagnostics.ForceRemoveException);
        pack.ModelRuntimeDriver.Verify(d => d.UnloadAsync(
            It.IsAny<DriverContext>(), It.IsAny<ModelReference>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));

        // The owned runner must now be disposed: any inference call fails fast.
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => runner.ChatAsync("x", TestContext.Current.CancellationToken));
      }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task Diagnostics_BeforeInitialize_IsNull()
    {
      var pack = new MockDriverPack().EnableModelDrivers();
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack);
      await using (kernel)
      {
        var resource = new ModelResource(kernel, Model);

        Assert.Null(resource.Diagnostics);
      }
    }

    [Fact]
    public async Task InitializeAsync_LoadFailure_Throws()
    {
      var pack = new MockDriverPack()
          .SetupModelUnload()
          .EnableModelDrivers();
      pack.ModelRuntimeDriver
          .Setup(d => d.LoadAsync(It.IsAny<DriverContext>(), It.IsAny<ModelReference>(),
              It.IsAny<ModelRunOptions>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Fail("load failed", ErrorCodes.Model.LoadFailed));
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack);
      await using (kernel)
      {
        var resource = new ModelResource(kernel, Model);

        await Assert.ThrowsAsync<ModelRunnerException>(
            () => resource.InitializeAsync(TestContext.Current.CancellationToken));
        Assert.False(resource.IsInitialized);
      }
    }
  }
}
