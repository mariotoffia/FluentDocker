using System;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Testing.Core;
using FluentDocker.Testing.Xunit;
using FluentDocker.Tests.Mocks;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Testing.Adapters
{
  [Trait("Category", "Unit")]
  public class XunitContainerFixtureBaseTests
  {
    [Fact]
    public async Task Lifecycle_InitializesAndDisposesCorrectly()
    {
      var (kernel, mockPack) = await MockKernelBuilderExtensions
          .CreateWithMockDriverAsync();
      mockPack
          .SetupContainerCreate()
          .SetupContainerStart()
          .SetupContainerInspect(running: true)
          .SetupContainerStop()
          .SetupContainerRemove();

      var fixture = new TestContainerFixture(
          () => Task.FromResult(kernel));

      await fixture.InitializeAsync();
      Assert.NotNull(fixture.Resource);
      Assert.True(fixture.Resource.IsInitialized);
      Assert.NotNull(fixture.Container);
      Assert.NotNull(fixture.Kernel);

      await fixture.DisposeAsync();
      Assert.Null(fixture.Resource);
      Assert.Null(fixture.Kernel);
    }

    [Fact]
    public async Task DisposeAsync_BeforeInit_DoesNotThrow()
    {
      var fixture = new TestContainerFixture(null);
      await fixture.DisposeAsync();
    }

    [Fact]
    public async Task CustomKernelFactory_IsUsed()
    {
      var (kernel, mockPack) = await MockKernelBuilderExtensions
          .CreateWithMockDriverAsync();
      mockPack
          .SetupContainerCreate()
          .SetupContainerStart()
          .SetupContainerInspect(running: true)
          .SetupContainerStop()
          .SetupContainerRemove();

      var factoryCalled = false;
      var fixture = new TestContainerFixture(() =>
      {
        factoryCalled = true;
        return Task.FromResult(kernel);
      });

      await fixture.InitializeAsync();
      Assert.True(factoryCalled);

      await fixture.DisposeAsync();
    }

    [Fact]
    public async Task DoubleInit_ThrowsInvalidOperationException()
    {
      var (kernel, mockPack) = await MockKernelBuilderExtensions
          .CreateWithMockDriverAsync();
      mockPack
          .SetupContainerCreate()
          .SetupContainerStart()
          .SetupContainerInspect(running: true)
          .SetupContainerStop()
          .SetupContainerRemove();

      var fixture = new TestContainerFixture(
          () => Task.FromResult(kernel));

      await fixture.InitializeAsync();

      await Assert.ThrowsAsync<InvalidOperationException>(
          () => fixture.InitializeAsync().AsTask());

      await fixture.DisposeAsync();
    }

    [Fact]
    public void DefaultKernelFactory_IsNull()
    {
      var fixture = new TestContainerFixture(null);
      Assert.Null(fixture.ExposedKernelFactory);
    }

    private class TestContainerFixture(
        Func<Task<FluentDockerKernel>> kernelFactory) : XunitContainerFixtureBase
    {
      private readonly Func<Task<FluentDockerKernel>> _kernelFactory = kernelFactory;

      protected override void ConfigureContainer(IContainerBuilder builder)
          => builder.UseImage("alpine:latest");

      protected override Func<Task<FluentDockerKernel>> KernelFactory
          => _kernelFactory;

      public Func<Task<FluentDockerKernel>> ExposedKernelFactory
          => base.KernelFactory;
    }
  }

  [Trait("Category", "Unit")]
  public class XunitComposeFixtureBaseTests
  {
    [Fact]
    public async Task Lifecycle_InitializesAndDisposesCorrectly()
    {
      var (kernel, mockPack) = await MockKernelBuilderExtensions
          .CreateWithMockDriverAsync();
      mockPack.SetupComposeUpAsync(new ComposeUpResult
      {
        ProjectName = "fixture-base-compose"
      });
      mockPack.SetupComposeStart();
      mockPack.SetupComposeStop();
      mockPack.SetupComposeDown();

      var fixture = new TestComposeFixture(
          () => Task.FromResult(kernel));

      await fixture.InitializeAsync();
      Assert.NotNull(fixture.Resource);
      Assert.True(fixture.Resource.IsInitialized);
      Assert.NotNull(fixture.Service);
      Assert.NotNull(fixture.Kernel);

      await fixture.DisposeAsync();
      Assert.Null(fixture.Resource);
      Assert.Null(fixture.Kernel);
    }

    [Fact]
    public async Task DoubleInit_ThrowsInvalidOperationException()
    {
      var (kernel, mockPack) = await MockKernelBuilderExtensions
          .CreateWithMockDriverAsync();
      mockPack.SetupComposeUpAsync(new ComposeUpResult
      {
        ProjectName = "fixture-base-compose"
      });
      mockPack.SetupComposeStart();
      mockPack.SetupComposeStop();
      mockPack.SetupComposeDown();

      var fixture = new TestComposeFixture(
          () => Task.FromResult(kernel));

      await fixture.InitializeAsync();

      await Assert.ThrowsAsync<InvalidOperationException>(
          () => fixture.InitializeAsync().AsTask());

      await fixture.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_BeforeInit_DoesNotThrow()
    {
      var fixture = new TestComposeFixture(null);
      await fixture.DisposeAsync();
    }

    private class TestComposeFixture(
        Func<Task<FluentDockerKernel>> kernelFactory) : XunitComposeFixtureBase
    {
      private readonly Func<Task<FluentDockerKernel>> _kernelFactory = kernelFactory;

      protected override void ConfigureCompose(IComposeBuilder builder)
          => builder.WithComposeFile("/path/docker-compose.yml")
              .WithProjectName("fixture-base-compose");

      protected override Func<Task<FluentDockerKernel>> KernelFactory
          => _kernelFactory;
    }
  }

  [Trait("Category", "Unit")]
  public class XunitTopologyFixtureBaseTests
  {
    [Fact]
    public async Task Lifecycle_InitializesAndDisposesCorrectly()
    {
      var (kernel, mockPack) = await MockKernelBuilderExtensions
          .CreateWithMockDriverAsync();
      mockPack
          .SetupContainerCreate()
          .SetupContainerStart()
          .SetupContainerInspect(running: true)
          .SetupContainerStop()
          .SetupContainerRemove();

      var fixture = new TestTopologyFixture(
          () => Task.FromResult(kernel));

      await fixture.InitializeAsync();
      Assert.NotNull(fixture.Resource);
      Assert.True(fixture.Resource.IsInitialized);
      Assert.NotNull(fixture.Kernel);

      await fixture.DisposeAsync();
      Assert.Null(fixture.Resource);
      Assert.Null(fixture.Kernel);
    }

    [Fact]
    public async Task DoubleInit_ThrowsInvalidOperationException()
    {
      var (kernel, mockPack) = await MockKernelBuilderExtensions
          .CreateWithMockDriverAsync();
      mockPack
          .SetupContainerCreate()
          .SetupContainerStart()
          .SetupContainerInspect(running: true)
          .SetupContainerStop()
          .SetupContainerRemove();

      var fixture = new TestTopologyFixture(
          () => Task.FromResult(kernel));

      await fixture.InitializeAsync();

      await Assert.ThrowsAsync<InvalidOperationException>(
          () => fixture.InitializeAsync().AsTask());

      await fixture.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_BeforeInit_DoesNotThrow()
    {
      var fixture = new TestTopologyFixture(null);
      await fixture.DisposeAsync();
    }

    private class TestTopologyFixture(
        Func<Task<FluentDockerKernel>> kernelFactory) : XunitTopologyFixtureBase
    {
      private readonly Func<Task<FluentDockerKernel>> _kernelFactory = kernelFactory;

      protected override void ConfigureTopology(Builder builder)
          => builder.UseContainer(c => c.UseImage("alpine:latest"));

      protected override Func<Task<FluentDockerKernel>> KernelFactory
          => _kernelFactory;
    }
  }
}
