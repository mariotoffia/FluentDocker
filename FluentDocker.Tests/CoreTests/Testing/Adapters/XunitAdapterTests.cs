using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Podman;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using FluentDocker.Testing.Core;
using FluentDocker.Testing.Xunit;
using FluentDocker.Tests.Mocks;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Testing.Adapters
{
  [Trait("Category", "Unit")]
  public class XunitContainerFixtureTests : MockKernelTestBase, IAsyncLifetime
  {
    public async Task InitializeAsync()
    {
      await InitializeMockKernelAsync();
    }

    public Task DisposeAsync()
    {
      return base.DisposeAsync().AsTask();
    }

    [Fact]
    public void PropertiesBeforeInit_ReturnNull()
    {
      var fixture = new XunitContainerFixture();
      Assert.Null(fixture.Resource);
      Assert.Null(fixture.Container);
      Assert.Null(fixture.Kernel);
    }

    [Fact]
    public async Task InitializeAsync_WithCustomKernelFactory_UsesProvidedKernel()
    {
      MockPack
          .SetupContainerCreate()
          .SetupContainerStart()
          .SetupContainerInspect(running: true)
          .SetupContainerStop()
          .SetupContainerRemove();

      var fixture = new XunitContainerFixture();
      var capturedKernel = Kernel;

      await fixture.InitializeAsync(
          configure: c => c.UseImage("redis:alpine"),
          kernelFactory: () => Task.FromResult(capturedKernel));

      Assert.NotNull(fixture.Resource);
      Assert.Same(capturedKernel, fixture.Kernel);
      Assert.True(fixture.Resource.IsInitialized);

      await fixture.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_CleansUpResource()
    {
      MockPack
          .SetupContainerCreate()
          .SetupContainerStart()
          .SetupContainerInspect(running: true)
          .SetupContainerStop()
          .SetupContainerRemove();

      var fixture = new XunitContainerFixture();

      await fixture.InitializeAsync(
          configure: c => c.UseImage("alpine:latest"),
          kernelFactory: () => Task.FromResult(Kernel));

      Assert.True(fixture.Resource.IsInitialized);

      await fixture.DisposeAsync();
      Assert.False(fixture.Resource.IsInitialized);
    }

    [Fact]
    public async Task DisposeAsync_BeforeInit_DoesNotThrow()
    {
      var fixture = new XunitContainerFixture();
      // Should not throw even when never initialized
      await fixture.DisposeAsync();
    }

    [Fact]
    public async Task InitializeAsync_CalledTwice_ThrowsInvalidOperationException()
    {
      MockPack
          .SetupContainerCreate()
          .SetupContainerStart()
          .SetupContainerInspect(running: true)
          .SetupContainerStop()
          .SetupContainerRemove();

      var fixture = new XunitContainerFixture();

      await fixture.InitializeAsync(
          configure: c => c.UseImage("redis:alpine"),
          kernelFactory: () => Task.FromResult(Kernel));

      await Assert.ThrowsAsync<InvalidOperationException>(() =>
          fixture.InitializeAsync(
              configure: c => c.UseImage("redis:alpine"),
              kernelFactory: () => Task.FromResult(Kernel)));

      await fixture.DisposeAsync();
    }
  }

  [Trait("Category", "Unit")]
  public class XunitSwarmStackFixtureTests : MockKernelTestBase, IAsyncLifetime
  {
    public async Task InitializeAsync()
    {
      await InitializeMockKernelAsync();
    }

    public Task DisposeAsync()
    {
      return base.DisposeAsync().AsTask();
    }

    [Fact]
    public void PropertiesBeforeInit_ReturnNull()
    {
      var fixture = new XunitSwarmStackFixture();
      Assert.Null(fixture.Resource);
      Assert.Null(fixture.StackName);
      Assert.Null(fixture.Kernel);
    }

    [Fact]
    public async Task InitializeAsync_WithCustomKernelFactory_UsesProvidedKernel()
    {
      MockPack.SetCapabilities(new DriverCapabilities
      {
        SupportsContainers = true,
        SupportsStacks = true
      });
      MockPack.EnableStackDriver();
      MockPack.SetupStackDeploy("fixture-stack");
      MockPack.SetupStackRemove();

      var fixture = new XunitSwarmStackFixture();
      var capturedKernel = Kernel;

      await fixture.InitializeAsync(
          new StackDeployConfig { StackName = "fixture-stack" },
          kernelFactory: () => Task.FromResult(capturedKernel));

      Assert.NotNull(fixture.Resource);
      Assert.Same(capturedKernel, fixture.Kernel);
      Assert.Equal("fixture-stack", fixture.StackName);

      await fixture.DisposeAsync();
    }
  }

  [Trait("Category", "Unit")]
  public class XunitPodmanKubernetesFixtureTests : MockKernelTestBase, IAsyncLifetime
  {
    public async Task InitializeAsync()
    {
      await InitializeMockKernelAsync();
    }

    public Task DisposeAsync()
    {
      return base.DisposeAsync().AsTask();
    }

    [Fact]
    public void PropertiesBeforeInit_ReturnNull()
    {
      var fixture = new XunitPodmanKubernetesFixture();
      Assert.Null(fixture.Resource);
      Assert.Null(fixture.YamlPath);
      Assert.Null(fixture.Kernel);
    }

    [Fact]
    public async Task InitializeAsync_WithCustomKernelFactory_UsesProvidedKernel()
    {
      MockPack.SetCapabilities(new DriverCapabilities
      {
        SupportsContainers = true,
        SupportsKubernetes = true
      });
      MockPack.EnablePodmanKubernetesDriver();
      MockPack.SetupKubePlay("fixture.yaml");
      MockPack.SetupKubeDown();

      var fixture = new XunitPodmanKubernetesFixture();
      var capturedKernel = Kernel;

      await fixture.InitializeAsync(
          new KubePlayConfig { YamlPath = "fixture.yaml" },
          kernelFactory: () => Task.FromResult(capturedKernel));

      Assert.NotNull(fixture.Resource);
      Assert.Same(capturedKernel, fixture.Kernel);
      Assert.Equal("fixture.yaml", fixture.YamlPath);

      await fixture.DisposeAsync();
    }
  }
}
