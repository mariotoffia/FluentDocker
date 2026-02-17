using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using FluentDocker.Testing.Core;
using FluentDocker.Testing.Core.Plugins;
using FluentDocker.Tests.Mocks;
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

    private class ThrowingResource : ITestResource
    {
      private readonly Exception _exception;

      public ThrowingResource(Exception exception) => _exception = exception;

      public bool IsInitialized => true;
      public Task InitializeAsync(CancellationToken cancellationToken = default)
          => Task.CompletedTask;

      public ValueTask DisposeAsync()
          => new ValueTask(Task.FromException(_exception));
    }

    private sealed class ThrowingKernel : FluentDockerKernel
    {
      private readonly Exception _exception;
      public ThrowingKernel(Exception exception) => _exception = exception;
      public override ValueTask DisposeAsync()
          => new ValueTask(Task.FromException(_exception));
    }

    private class FakeResource : ITestResource
    {
      public bool IsInitialized => false;
      public Task InitializeAsync(CancellationToken cancellationToken = default)
          => Task.CompletedTask;
      public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private class SingleKeyPlugin : ITestPlugin
    {
      private readonly string _key;
      public string Id { get; }

      public SingleKeyPlugin(string id, string key)
      {
        Id = id;
        _key = key;
      }

      public void Register(ITestPluginRegistry registry)
      {
        registry.RegisterFactory<FakeResource>(_key, _ => new FakeResource());
      }
    }

    private class DualKeyPlugin : ITestPlugin
    {
      private readonly string _key1;
      private readonly string _key2;
      public string Id { get; }

      public DualKeyPlugin(string id, string key1, string key2)
      {
        Id = id;
        _key1 = key1;
        _key2 = key2;
      }

      public void Register(ITestPluginRegistry registry)
      {
        registry.RegisterFactory<FakeResource>(_key1, _ => new FakeResource());
        registry.RegisterFactory<FakeResource>(_key2, _ => new FakeResource());
      }
    }

    #endregion
  }
}
