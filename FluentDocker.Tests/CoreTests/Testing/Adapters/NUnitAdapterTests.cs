using System;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using FluentDocker.Testing.Core;
using FluentDocker.Testing.NUnit;
using FluentDocker.Tests.Mocks;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Testing.Adapters
{
  [Trait("Category", "Unit")]
  public class NUnitAdapterTests : MockKernelTestBase, IAsyncLifetime
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
    public async Task CreateContainerAsync_WithCustomKernel_ReturnsInitializedResource()
    {
      MockPack
          .SetupContainerCreate()
          .SetupContainerStart()
          .SetupContainerInspect(running: true)
          .SetupContainerStop()
          .SetupContainerRemove();

      var (kernel, resource) = await NUnitResourceHelpers.CreateContainerAsync(
          configure: c => c.UseImage("redis:alpine"),
          kernelFactory: () => Task.FromResult(Kernel));

      Assert.True(resource.IsInitialized);
      Assert.Same(Kernel, kernel);

      await NUnitResourceHelpers.DisposeAsync(resource, null);
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
      MockPack.SetupStackDeploy("nunit-stack");
      MockPack.SetupStackRemove();

      var (kernel, resource) = await NUnitResourceHelpers.CreateSwarmStackAsync(
          new StackDeployConfig { StackName = "nunit-stack" },
          kernelFactory: () => Task.FromResult(Kernel));

      Assert.True(resource.IsInitialized);
      Assert.Equal("nunit-stack", resource.StackName);
      Assert.Same(Kernel, kernel);

      await NUnitResourceHelpers.DisposeAsync(resource, null);
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
      MockPack.SetupKubePlay("nunit.yaml");
      MockPack.SetupKubeDown();

      var (kernel, resource) = await NUnitResourceHelpers.CreatePodmanKubernetesAsync(
          new FluentDocker.Drivers.Podman.KubePlayConfig { YamlPath = "nunit.yaml" },
          kernelFactory: () => Task.FromResult(Kernel));

      Assert.True(resource.IsInitialized);
      Assert.Equal("nunit.yaml", resource.YamlPath);
      Assert.Same(Kernel, kernel);

      await NUnitResourceHelpers.DisposeAsync(resource, null);
    }

    [Fact]
    public async Task DisposeAsync_NullResourceAndKernel_DoesNotThrow()
    {
      await NUnitResourceHelpers.DisposeAsync(null, null);
    }

    [Fact]
    public async Task DisposeAsync_NullResource_DisposesKernel()
    {
      var (kernel, _) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("dispose-test");

      await NUnitResourceHelpers.DisposeAsync(null, kernel);
    }
  }
}
