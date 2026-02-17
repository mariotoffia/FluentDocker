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
    public async Task UnconfiguredFixture_LifecycleInit_IsNoOp()
    {
      var fixture = new XunitContainerFixture();

      // xUnit calls InitializeAsync on unconfigured fixture — should not throw
      await ((IAsyncLifetime)fixture).InitializeAsync();

      // Properties should still throw since nothing was initialized
      Assert.Throws<InvalidOperationException>(() => _ = fixture.Resource);
      Assert.Throws<InvalidOperationException>(() => _ = fixture.Kernel);

      await fixture.DisposeAsync();
    }

    [Fact]
    public void Configure_NullConfigure_ThrowsArgumentNull()
    {
      var fixture = new XunitContainerFixture();
      Assert.Throws<ArgumentNullException>(() => fixture.Configure(null));
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
    public async Task UnconfiguredFixture_LifecycleInit_IsNoOp()
    {
      var fixture = new XunitResourceFixture<ContainerResource>();

      await ((IAsyncLifetime)fixture).InitializeAsync();

      Assert.Throws<InvalidOperationException>(() => _ = fixture.Resource);
      Assert.Throws<InvalidOperationException>(() => _ = fixture.Kernel);

      await fixture.DisposeAsync();
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
