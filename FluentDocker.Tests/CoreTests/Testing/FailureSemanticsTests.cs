using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Model.Drivers;
using FluentDocker.Testing.Core;
using FluentDocker.Testing.Core.Plugins;
using FluentDocker.Tests.Mocks;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Testing
{
  [Trait("Category", "Unit")]
  public class FailureSemanticsTests : MockKernelTestBase, IAsyncLifetime
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

      await resource.InitializeAsync();
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
          () => resource.InitializeAsync());

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

      await resource.InitializeAsync();

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

    #region Test Doubles

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
