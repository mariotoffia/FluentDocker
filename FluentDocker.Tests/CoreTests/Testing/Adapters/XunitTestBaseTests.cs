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
  public class XunitContainerTestBaseTests
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

      var testBase = new TestContainerTestBase(
          () => Task.FromResult(kernel));

      await testBase.InitializeAsync();
      Assert.NotNull(testBase.Resource);
      Assert.True(testBase.Resource.IsInitialized);
      Assert.NotNull(testBase.Container);
      Assert.NotNull(testBase.Kernel);

      await testBase.DisposeAsync();
      Assert.Null(testBase.Resource);
      Assert.Null(testBase.Kernel);
    }

    [Fact]
    public async Task DisposeAsync_BeforeInit_DoesNotThrow()
    {
      var testBase = new TestContainerTestBase(null);
      await testBase.DisposeAsync();
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

      var testBase = new TestContainerTestBase(
          () => Task.FromResult(kernel));

      await testBase.InitializeAsync();

      await Assert.ThrowsAsync<InvalidOperationException>(
          () => testBase.InitializeAsync().AsTask());

      await testBase.DisposeAsync();
    }

    [Fact]
    public async Task DefaultKernelFactory_IsNull()
    {
      var testBase = new TestContainerTestBase(null);
      Assert.Null(testBase.ExposedKernelFactory);
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
      var testBase = new TestContainerTestBase(() =>
      {
        factoryCalled = true;
        return Task.FromResult(kernel);
      });

      await testBase.InitializeAsync();
      Assert.True(factoryCalled);

      await testBase.DisposeAsync();
    }

    private class TestContainerTestBase : XunitContainerTestBase
    {
      private readonly Func<Task<FluentDockerKernel>> _kernelFactory;

      public TestContainerTestBase(Func<Task<FluentDockerKernel>> kernelFactory) => _kernelFactory = kernelFactory;

      protected override void ConfigureContainer(IContainerBuilder builder)
          => builder.UseImage("alpine:latest");

      protected override Func<Task<FluentDockerKernel>> KernelFactory
          => _kernelFactory;

      public Func<Task<FluentDockerKernel>> ExposedKernelFactory
          => base.KernelFactory;
    }
  }

  [Trait("Category", "Unit")]
  public class XunitComposeTestBaseTests
  {
    [Fact]
    public async Task DoubleInit_ThrowsInvalidOperationException()
    {
      var (kernel, mockPack) = await MockKernelBuilderExtensions
          .CreateWithMockDriverAsync();
      mockPack.SetupComposeUpAsync(new ComposeUpResult
      {
        ProjectName = "test-base-compose"
      });
      mockPack.SetupComposeStart();
      mockPack.SetupComposeStop();
      mockPack.SetupComposeDown();

      var testBase = new TestComposeTestBase(
          () => Task.FromResult(kernel));

      await testBase.InitializeAsync();

      await Assert.ThrowsAsync<InvalidOperationException>(
          () => testBase.InitializeAsync().AsTask());

      await testBase.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_BeforeInit_DoesNotThrow()
    {
      var testBase = new TestComposeTestBase(null);
      await testBase.DisposeAsync();
    }

    private class TestComposeTestBase : XunitComposeTestBase
    {
      private readonly Func<Task<FluentDockerKernel>> _kernelFactory;

      public TestComposeTestBase(
          Func<Task<FluentDockerKernel>> kernelFactory = null)
          => _kernelFactory = kernelFactory;

      protected override void ConfigureCompose(IComposeBuilder builder)
          => builder.WithComposeFile("docker-compose.yml")
              .WithProjectName("test-base-compose");

      protected override Func<Task<FluentDockerKernel>> KernelFactory
          => _kernelFactory;
    }
  }

  [Trait("Category", "Unit")]
  public class XunitTopologyTestBaseTests
  {
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

      var testBase = new TestTopologyTestBase(
          () => Task.FromResult(kernel));

      await testBase.InitializeAsync();

      await Assert.ThrowsAsync<InvalidOperationException>(
          () => testBase.InitializeAsync().AsTask());

      await testBase.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_BeforeInit_DoesNotThrow()
    {
      var testBase = new TestTopologyTestBase(null);
      await testBase.DisposeAsync();
    }

    private class TestTopologyTestBase : XunitTopologyTestBase
    {
      private readonly Func<Task<FluentDockerKernel>> _kernelFactory;

      public TestTopologyTestBase(
          Func<Task<FluentDockerKernel>> kernelFactory = null)
          => _kernelFactory = kernelFactory;

      protected override void ConfigureTopology(Builder builder)
          => builder.UseContainer(c => c.UseImage("alpine:latest"));

      protected override Func<Task<FluentDockerKernel>> KernelFactory
          => _kernelFactory;
    }
  }
}
