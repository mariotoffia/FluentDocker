using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Containers;
using FluentDocker.Model.Drivers;
using FluentDocker.Testing.Core;
using FluentDocker.Testing.MsTest;
using FluentDocker.Tests.Mocks;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Testing.Adapters
{
  [Trait("Category", "Unit")]
  public class MsTestAdapterTests : MockKernelTestBase, IAsyncLifetime
  {
    public async ValueTask InitializeAsync()
    {
      await InitializeMockKernelAsync();
    }

    [Fact]
    public async Task CreateContainerAsync_WithCustomKernel_ReturnsInitializedResource()
    {
      MockPack
          .SetupContainerCreate()
          .SetupContainerStart()
          .SetupContainerInspect(running: true)
          .SetupContainerStop()
          .SetupContainerRemove();

      var (kernel, resource) = await MsTestResourceHelpers.CreateContainerAsync(
          configure: c => c.UseImage("redis:alpine"),
          kernelFactory: () => Task.FromResult(Kernel));

      Assert.True(resource.IsInitialized);
      Assert.Same(Kernel, kernel);

      await MsTestResourceHelpers.DisposeAsync(resource, null);
    }

    [Fact]
    public async Task CreateSwarmStackAsync_WithCustomKernel_ReturnsInitializedResource()
    {
      MockPack.SetCapabilities(new DriverCapabilities
      {
        SupportsContainers = true,
        SupportsStacks = true
      });
      MockPack.EnableStackDriver();
      MockPack.SetupStackDeploy("mstest-stack");
      MockPack.SetupStackRemove();

      var (kernel, resource) = await MsTestResourceHelpers.CreateSwarmStackAsync(
          new StackDeployConfig { StackName = "mstest-stack" },
          kernelFactory: () => Task.FromResult(Kernel));

      Assert.True(resource.IsInitialized);
      Assert.Equal("mstest-stack", resource.StackName);
      Assert.Same(Kernel, kernel);

      await MsTestResourceHelpers.DisposeAsync(resource, null);
    }

    [Fact]
    public async Task CreatePodmanKubernetesAsync_WithCustomKernel_ReturnsInitializedResource()
    {
      MockPack.SetCapabilities(new DriverCapabilities
      {
        SupportsContainers = true,
        SupportsKubernetes = true
      });
      MockPack.EnablePodmanKubernetesDriver();
      MockPack.SetupKubePlay("mstest.yaml");
      MockPack.SetupKubeDown();

      var (kernel, resource) = await MsTestResourceHelpers.CreatePodmanKubernetesAsync(
          new FluentDocker.Drivers.Podman.KubePlayConfig { YamlPath = "mstest.yaml" },
          kernelFactory: () => Task.FromResult(Kernel));

      Assert.True(resource.IsInitialized);
      Assert.Equal("mstest.yaml", resource.YamlPath);
      Assert.Same(Kernel, kernel);

      await MsTestResourceHelpers.DisposeAsync(resource, null);
    }

    [Fact]
    public async Task DisposeAsync_NullResourceAndKernel_DoesNotThrow()
    {
      await MsTestResourceHelpers.DisposeAsync(null, null);
    }

    [Fact]
    public async Task DisposeAsync_NullResource_DisposesKernel()
    {
      // Create a separate kernel to avoid affecting other tests
      var (kernel, _) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("dispose-test");

      await MsTestResourceHelpers.DisposeAsync(null, kernel);
      // kernel.Dispose() was called; no exception
    }

    [Fact]
    public async Task CreateContainerAsync_WhenInitFails_CleansUpKernel()
    {
      var (testKernel, testPack) =
          await MockKernelBuilderExtensions.CreateWithMockDriverAsync("fail-test");

      testPack.ContainerDriver
          .Setup(d => d.CreateAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<ContainerCreateConfig>(),
              It.IsAny<CancellationToken>()))
          .ThrowsAsync(new InvalidOperationException("create failed"));

      await Assert.ThrowsAsync<InvalidOperationException>(() =>
          MsTestResourceHelpers.CreateContainerAsync(
              configure: c => c.UseImage("fail:image"),
              kernelFactory: () => Task.FromResult(testKernel)));
    }

    [Fact]
    public async Task CreateResourceAsync_SuccessPath_ReturnsInitializedResource()
    {
      MockPack
          .SetupContainerCreate()
          .SetupContainerStart()
          .SetupContainerInspect(running: true)
          .SetupContainerStop()
          .SetupContainerRemove();

      var (kernel, resource) = await MsTestResourceHelpers.CreateResourceAsync<ContainerResource>(
          k => new ContainerResource(k, c => c.UseImage("alpine:latest")),
          kernelFactory: () => Task.FromResult(Kernel));

      Assert.True(resource.IsInitialized);
      Assert.Same(Kernel, kernel);

      await MsTestResourceHelpers.DisposeAsync(resource, null);
    }

    [Fact]
    public async Task CreateResourceAsync_NullFactory_ThrowsArgumentNullException()
    {
      await Assert.ThrowsAsync<ArgumentNullException>(() =>
          MsTestResourceHelpers.CreateResourceAsync<ContainerResource>(null!));
    }

    [Fact]
    public async Task CreateResourceAsync_FactoryReturnsNull_ThrowsInvalidOperationException()
    {
      var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
          MsTestResourceHelpers.CreateResourceAsync<ContainerResource>(
              _ => null!,
              kernelFactory: () => Task.FromResult(Kernel)));

      Assert.Contains("resourceFactory returned null", ex.Message);
    }

    [Fact]
    public async Task CreateResourceAsync_WhenInitFails_CleansUpKernel()
    {
      var (testKernel, testPack) =
          await MockKernelBuilderExtensions.CreateWithMockDriverAsync("fail-generic");

      testPack.ContainerDriver
          .Setup(d => d.CreateAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<ContainerCreateConfig>(),
              It.IsAny<CancellationToken>()))
          .ThrowsAsync(new InvalidOperationException("create failed"));

      await Assert.ThrowsAsync<InvalidOperationException>(() =>
          MsTestResourceHelpers.CreateResourceAsync<ContainerResource>(
              k => new ContainerResource(k, c => c.UseImage("fail:img")),
              kernelFactory: () => Task.FromResult(testKernel)));
    }
  }
}
