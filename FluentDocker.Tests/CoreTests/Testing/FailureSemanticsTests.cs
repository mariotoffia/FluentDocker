using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using FluentDocker.Testing.Core;
using FluentDocker.Testing.Core.Plugins;
using FluentDocker.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Testing
{
  [Trait("Category", "Unit")]
  public partial class FailureSemanticsTests : MockKernelTestBase, IAsyncLifetime
  {
    public async ValueTask InitializeAsync()
    {
      await InitializeMockKernelAsync();
    }

    [Fact]
    public async Task TeardownFailure_TriggersForceRemove_WhenForceRemoveOnDispose()
    {
      // Setup: create succeeds, start succeeds, inspect succeeds,
      // stop FAILS (simulates teardown failure), remove succeeds (force remove).
      MockPack
          .SetupContainerCreate()
          .SetupContainerStart()
          .SetupContainerInspect(running: true)
          .SetupContainerRemove();

      // Make stop throw to trigger teardown failure
      MockPack.ContainerDriver
          .Setup(d => d.StopAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              It.IsAny<int?>(),
              It.IsAny<CancellationToken>()))
          .ThrowsAsync(new InvalidOperationException("Simulated stop failure"));

      var resource = new ContainerResource(
          Kernel,
          builder => builder.UseImage("alpine:latest"),
          new DockerResourceOptions { ForceRemoveOnDispose = true });

      await resource.InitializeAsync(TestContext.Current.CancellationToken);
      Assert.True(resource.IsInitialized);

      // Dispose should not throw — teardown fails but force-remove kicks in
      await resource.DisposeAsync();
      Assert.False(resource.IsInitialized);

      // Verify force-remove was called (RemoveAsync with force: true)
      MockPack.ContainerDriver.Verify(
          d => d.RemoveAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              true, // force = true
              It.IsAny<bool>(),
              It.IsAny<CancellationToken>()),
          Times.AtLeastOnce());
    }

    [Fact]
    public async Task ForceRemove_GetsFreshToken_WhenTeardownTimesOut()
    {
      MockPack
          .SetupContainerCreate()
          .SetupContainerStart()
          .SetupContainerInspect(running: true);

      // Make stop delay beyond the teardown timeout so the CTS is canceled
      MockPack.ContainerDriver
          .Setup(d => d.StopAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              It.IsAny<int?>(),
              It.IsAny<CancellationToken>()))
          .Returns<DriverContext, string, int?, CancellationToken>(
              async (_, _, _, ct) => { await Task.Delay(5000, ct); return default; });

      // Capture the token force-remove receives
      CancellationToken capturedToken = default;
      MockPack.ContainerDriver
          .Setup(d => d.RemoveAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              true, // force = true (from ForceRemoveAsync)
              It.IsAny<bool>(),
              It.IsAny<CancellationToken>()))
          .Returns<DriverContext, string, bool, bool, CancellationToken>(
              (_, _, _, _, ct) =>
              {
                capturedToken = ct;
                return Task.FromResult(
                    FluentDocker.Model.Drivers.CommandResponse<FluentDocker.Model.Drivers.Unit>
                        .Ok(FluentDocker.Model.Drivers.Unit.Default));
              });

      var resource = new ContainerResource(
          Kernel,
          builder => builder.UseImage("alpine:latest"),
          new DockerResourceOptions
          {
            ForceRemoveOnDispose = true,
            TeardownTimeout = TimeSpan.FromMilliseconds(200)
          });

      await resource.InitializeAsync(TestContext.Current.CancellationToken);

      // Graceful teardown will timeout → force-remove should get a fresh token
      await resource.DisposeAsync();

      Assert.False(capturedToken.IsCancellationRequested,
          "Force-remove received a canceled token; it should get a fresh timeout.");
    }

    [Fact]
    public async Task TeardownFailure_Propagates_WhenForceRemoveOnDisposeIsFalse()
    {
      MockPack
          .SetupContainerCreate()
          .SetupContainerStart()
          .SetupContainerInspect(running: true)
          .SetupContainerRemove();

      // Make stop throw to trigger teardown failure
      MockPack.ContainerDriver
          .Setup(d => d.StopAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              It.IsAny<int?>(),
              It.IsAny<CancellationToken>()))
          .ThrowsAsync(new InvalidOperationException("Simulated stop failure"));

      var resource = new ContainerResource(
          Kernel,
          builder => builder.UseImage("alpine:latest"),
          new DockerResourceOptions { ForceRemoveOnDispose = false });

      await resource.InitializeAsync(TestContext.Current.CancellationToken);
      Assert.True(resource.IsInitialized);

      // Dispose should throw because ForceRemoveOnDispose is false
      var ex = await Assert.ThrowsAsync<InvalidOperationException>(
          () => resource.DisposeAsync().AsTask());
      Assert.Contains("stop failure", ex.Message);

      // IsInitialized should still be set to false
      Assert.False(resource.IsInitialized);
    }

    [Fact]
    public async Task TeardownFails_ForceRemoveSucceeds_CapturesTeardownDiagnostics()
    {
      MockPack
          .SetupContainerCreate()
          .SetupContainerStart()
          .SetupContainerInspect(running: true)
          .SetupContainerRemove();

      MockPack.ContainerDriver
          .Setup(d => d.StopAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              It.IsAny<int?>(),
              It.IsAny<CancellationToken>()))
          .ThrowsAsync(new InvalidOperationException("stop failed"));

      var resource = new ContainerResource(
          Kernel,
          builder => builder.UseImage("alpine:latest"),
          new DockerResourceOptions { ForceRemoveOnDispose = true });

      await resource.InitializeAsync(TestContext.Current.CancellationToken);
      await resource.DisposeAsync();

      Assert.NotNull(resource.LastTeardownDiagnostics);
      Assert.IsType<InvalidOperationException>(resource.LastTeardownDiagnostics.TeardownException);
      Assert.Null(resource.LastTeardownDiagnostics.ForceRemoveException);
    }

    [Fact]
    public async Task TeardownFails_ForceRemoveAlsoFails_CapturesBothExceptions()
    {
      var resource = new FailingTeardownResource(
          Kernel,
          teardownEx: new InvalidOperationException("teardown failed"),
          forceRemoveEx: new InvalidOperationException("force-remove failed"),
          new DockerResourceOptions { ForceRemoveOnDispose = true });

      await resource.InitializeAsync(TestContext.Current.CancellationToken);
      await resource.DisposeAsync();

      Assert.NotNull(resource.LastTeardownDiagnostics);
      Assert.NotNull(resource.LastTeardownDiagnostics.TeardownException);
      Assert.NotNull(resource.LastTeardownDiagnostics.ForceRemoveException);
      Assert.Contains("teardown failed", resource.LastTeardownDiagnostics.TeardownException.Message);
      Assert.Contains("force-remove failed", resource.LastTeardownDiagnostics.ForceRemoveException.Message);
    }

    [Fact]
    public async Task TeardownSucceeds_DiagnosticsIsNull()
    {
      MockPack
          .SetupContainerCreate()
          .SetupContainerStart()
          .SetupContainerInspect(running: true)
          .SetupContainerStop()
          .SetupContainerRemove();

      var resource = new ContainerResource(
          Kernel,
          builder => builder.UseImage("alpine:latest"));

      await resource.InitializeAsync(TestContext.Current.CancellationToken);
      await resource.DisposeAsync();

      Assert.Null(resource.LastTeardownDiagnostics);
    }

    [Fact]
    public async Task OnAfterReady_Throws_IsInitializedRemainsFalse()
    {
      MockPack
          .SetupContainerCreate()
          .SetupContainerStart()
          .SetupContainerInspect(running: true)
          .SetupContainerStop()
          .SetupContainerRemove();

      var resource = new ContainerResource(
          Kernel,
          builder => builder.UseImage("alpine:latest"));

      resource.OnAfterReady(_ =>
          throw new InvalidOperationException("Hook failure"));

      await Assert.ThrowsAsync<InvalidOperationException>(
          () => resource.InitializeAsync(TestContext.Current.CancellationToken));

      // IsInitialized must be false because the hook threw
      Assert.False(resource.IsInitialized);

      // Diagnostics should have been collected
      Assert.NotNull(resource.Diagnostics);
    }

    [Fact]
    public async Task OnAfterReady_Success_IsInitializedIsTrue()
    {
      MockPack
          .SetupContainerCreate()
          .SetupContainerStart()
          .SetupContainerInspect(running: true)
          .SetupContainerStop()
          .SetupContainerRemove();

      var hookCalled = false;
      var resource = new ContainerResource(
          Kernel,
          builder => builder.UseImage("alpine:latest"));

      resource.OnAfterReady(_ =>
      {
        hookCalled = true;
        return Task.CompletedTask;
      });

      await resource.InitializeAsync(TestContext.Current.CancellationToken);

      Assert.True(hookCalled);
      Assert.True(resource.IsInitialized);

      await resource.DisposeAsync();
    }

    [Fact]
    public void PluginRegistration_PartialFailure_RollsBackAllFactories()
    {
      var host = new TestPluginHost();

      // First plugin registers key "alpha"
      host.Add(new SingleKeyPlugin("plugin-a", "alpha"));

      // Second plugin tries to register "beta" (new) and "alpha" (collision)
      // The staging mechanism should prevent "beta" from being committed
      var ex = Assert.Throws<InvalidOperationException>(
          () => host.Add(new DualKeyPlugin("plugin-b", "beta", "alpha")));

      Assert.Contains("already registered", ex.Message);

      // "beta" should NOT be available because the registration was rolled back
      Assert.False(host.HasFactory("beta"));

      // "alpha" should still be available from plugin-a
      Assert.True(host.HasFactory("alpha"));
    }

    [Fact]
    public void PluginRegistration_Success_CommitsAllFactories()
    {
      var host = new TestPluginHost();
      host.Add(new DualKeyPlugin("plugin-a", "key1", "key2"));

      Assert.True(host.HasFactory("key1"));
      Assert.True(host.HasFactory("key2"));
    }

    [Fact]
    public async Task NullDriver_ThrowsDescriptiveError()
    {
      MockPack
          .SetupContainerCreate()
          .SetupContainerStart()
          .SetupContainerInspect(running: true);

      var resource = new ContainerResource(
          Kernel,
          builder => builder.UseImage("alpine:latest"),
          new DockerResourceOptions { Driver = null });

      var ex = await Assert.ThrowsAsync<InvalidOperationException>(
          () => resource.InitializeAsync(TestContext.Current.CancellationToken));
      Assert.Contains("Driver is null", ex.Message);
    }

    [Fact]
    public async Task ResourceLifecycle_DisposeAsync_BothThrow_ProducesAggregateException()
    {
      // When both resource and kernel disposal throw,
      // ResourceLifecycle.DisposeAsync should wrap both in AggregateException.
      var throwingResource = new ThrowingResource(
          new InvalidOperationException("resource disposal failed"));
      var throwingKernel = new ThrowingKernel(
          new ObjectDisposedException("kernel disposal failed"));

      var agg = await Assert.ThrowsAsync<AggregateException>(
          () => ResourceLifecycle.DisposeAsync(throwingResource, throwingKernel));

      Assert.Equal(2, agg.InnerExceptions.Count);
      Assert.IsType<InvalidOperationException>(agg.InnerExceptions[0]);
      Assert.IsType<ObjectDisposedException>(agg.InnerExceptions[1]);
    }

    [Fact]
    public async Task ResourceLifecycle_DisposeAsync_OnlyResourceThrows_PreservesStackTrace()
    {
      var throwingResource = new ThrowingResource(
          new InvalidOperationException("resource only"));

      var (kernel, _) =
          await MockKernelBuilderExtensions.CreateWithMockDriverAsync("ok-driver");

      var ex = await Assert.ThrowsAsync<InvalidOperationException>(
          () => ResourceLifecycle.DisposeAsync(throwingResource, kernel));
      Assert.Contains("resource only", ex.Message);
    }

    #region Test Doubles

    private class FailingTeardownResource(
        FluentDockerKernel kernel,
        Exception teardownEx,
        Exception forceRemoveEx,
        DockerResourceOptions options) : ResourceBase(kernel, options)
    {
      private readonly Exception _teardownEx = teardownEx;
      private readonly Exception _forceRemoveEx = forceRemoveEx;

      protected override Task PreflightAsync(CancellationToken cancellationToken)
          => Task.CompletedTask;

      protected override Task ProvisionAsync(CancellationToken cancellationToken)
      {
        ResourceName = "test-failing-resource";
        return Task.CompletedTask;
      }

      protected override Task TeardownAsync(CancellationToken cancellationToken)
          => Task.FromException(_teardownEx);

      protected override Task ForceRemoveAsync(CancellationToken cancellationToken)
          => _forceRemoveEx != null
              ? Task.FromException(_forceRemoveEx)
              : Task.CompletedTask;
    }

    private class ThrowingResource(Exception exception) : ITestResource
    {
      private readonly Exception _exception = exception;

      public bool IsInitialized => true;
      public Task InitializeAsync(CancellationToken cancellationToken = default)
          => Task.CompletedTask;

      public ValueTask DisposeAsync()
          => new(Task.FromException(_exception));
    }

    private sealed class ThrowingKernel(Exception exception) : FluentDockerKernel(new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance)
    {
      private readonly Exception _exception = exception;

      public override async ValueTask DisposeAsync()
      {
        await base.DisposeAsync();
        throw _exception;
      }
    }

    private class FakeResource : ITestResource
    {
      public bool IsInitialized => false;
      public Task InitializeAsync(CancellationToken cancellationToken = default)
          => Task.CompletedTask;
      public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private class SingleKeyPlugin(string id, string key) : ITestPlugin
    {
      private readonly string _key = key;
      public string Id { get; } = id;

      public void Register(ITestPluginRegistry registry)
      {
        registry.RegisterFactory<FakeResource>(_key, _ => new FakeResource());
      }
    }

    private class DualKeyPlugin(string id, string key1, string key2) : ITestPlugin
    {
      private readonly string _key1 = key1;
      private readonly string _key2 = key2;
      public string Id { get; } = id;

      public void Register(ITestPluginRegistry registry)
      {
        registry.RegisterFactory<FakeResource>(_key1, _ => new FakeResource());
        registry.RegisterFactory<FakeResource>(_key2, _ => new FakeResource());
      }
    }

    #endregion
  }
}
