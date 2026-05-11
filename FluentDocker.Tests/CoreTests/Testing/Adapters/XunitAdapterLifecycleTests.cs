using System;
using System.Threading.Tasks;
using FluentDocker.Kernel;
using FluentDocker.Testing.Core;
using FluentDocker.Testing.Xunit;
using FluentDocker.Tests.Mocks;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Testing.Adapters
{
  /// <summary>
  /// Tests for the <see cref="IAsyncLifetime"/> + <see cref="XunitContainerFixture.Configure"/>
  /// pattern on concrete xUnit fixtures.
  /// </summary>
  [Trait("Category", "Unit")]
  public class XunitContainerFixtureLifecycleTests : MockKernelTestBase, IAsyncLifetime
  {
    public async ValueTask InitializeAsync()
    {
      await InitializeMockKernelAsync();
    }

    [Fact]
    public async Task Configure_ThenLifecycleInit_InitializesCorrectly()
    {
      MockPack
          .SetupContainerCreate()
          .SetupContainerStart()
          .SetupContainerInspect(running: true)
          .SetupContainerStop()
          .SetupContainerRemove();

      var capturedKernel = Kernel;
      var fixture = new XunitContainerFixture();
      fixture.Configure(
          c => c.UseImage("redis:alpine"),
          kernelFactory: () => Task.FromResult(capturedKernel));

      // Simulate xUnit calling IAsyncLifetime.InitializeAsync
      await ((IAsyncLifetime)fixture).InitializeAsync();

      Assert.NotNull(fixture.Resource);
      Assert.True(fixture.Resource.IsInitialized);
      Assert.Same(capturedKernel, fixture.Kernel);

      await fixture.DisposeAsync();
    }

    [Fact]
    public async Task UnconfiguredFixture_LifecycleInit_ThrowsInvalidOperation()
    {
      var fixture = new XunitContainerFixture();

      // xUnit calls InitializeAsync on unconfigured fixture — should throw
      var ex = await Assert.ThrowsAsync<InvalidOperationException>(
          () => ((IAsyncLifetime)fixture).InitializeAsync().AsTask());
      Assert.Contains("has not been configured", ex.Message);
    }

    [Fact]
    public void Configure_NullConfigure_ThrowsArgumentNull()
    {
      var fixture = new XunitContainerFixture();
      Assert.Throws<ArgumentNullException>(() => fixture.Configure(null));
    }

    [Fact]
    public async Task ManualInit_ThenLifecycleInit_NoOps()
    {
      MockPack
          .SetupContainerCreate()
          .SetupContainerStart()
          .SetupContainerInspect(running: true)
          .SetupContainerStop()
          .SetupContainerRemove();

      var capturedKernel = Kernel;
      var fixture = new XunitContainerFixture();

      // Manual init (old pattern)
      await fixture.InitializeAsync(
          c => c.UseImage("redis:alpine"),
          kernelFactory: () => Task.FromResult(capturedKernel),
          cancellationToken: TestContext.Current.CancellationToken);

      var resourceBefore = fixture.Resource;
      Assert.NotNull(resourceBefore);

      // xUnit also calls IAsyncLifetime.InitializeAsync — should no-op
      await ((IAsyncLifetime)fixture).InitializeAsync();

      Assert.Same(resourceBefore, fixture.Resource);

      await fixture.DisposeAsync();
    }
  }

  [Trait("Category", "Unit")]
  public class XunitResourceFixtureLifecycleTests : MockKernelTestBase, IAsyncLifetime
  {
    public async ValueTask InitializeAsync()
    {
      await InitializeMockKernelAsync();
    }

    [Fact]
    public async Task Configure_ThenLifecycleInit_InitializesCorrectly()
    {
      MockPack
          .SetupContainerCreate()
          .SetupContainerStart()
          .SetupContainerInspect(running: true)
          .SetupContainerStop()
          .SetupContainerRemove();

      var capturedKernel = Kernel;
      var fixture = new XunitResourceFixture<ContainerResource>();
      fixture.Configure(
          k => new ContainerResource(k, c => c.UseImage("alpine:latest")),
          kernelFactory: () => Task.FromResult(capturedKernel));

      await ((IAsyncLifetime)fixture).InitializeAsync();

      Assert.NotNull(fixture.Resource);
      Assert.True(fixture.Resource.IsInitialized);
      Assert.Same(capturedKernel, fixture.Kernel);

      await fixture.DisposeAsync();
    }

    [Fact]
    public async Task UnconfiguredFixture_LifecycleInit_ThrowsInvalidOperation()
    {
      var fixture = new XunitResourceFixture<ContainerResource>();

      var ex = await Assert.ThrowsAsync<InvalidOperationException>(
          () => ((IAsyncLifetime)fixture).InitializeAsync().AsTask());
      Assert.Contains("has not been configured", ex.Message);
    }

    [Fact]
    public void Configure_NullFactory_ThrowsArgumentNull()
    {
      var fixture = new XunitResourceFixture<ContainerResource>();
      Assert.Throws<ArgumentNullException>(
          () => fixture.Configure(null));
    }
  }
}
