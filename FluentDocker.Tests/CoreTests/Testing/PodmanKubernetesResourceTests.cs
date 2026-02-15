using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers.Podman;
using FluentDocker.Model.Drivers;
using FluentDocker.Testing.Core;
using FluentDocker.Tests.Mocks;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Testing
{
  [Trait("Category", "Unit")]
  public class PodmanKubernetesResourceTests : MockKernelTestBase, IAsyncLifetime
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
    public async Task InitializeAndDispose_Lifecycle_Succeeds()
    {
      MockPack.SetCapabilities(new DriverCapabilities
      {
        SupportsContainers = true,
        SupportsKubernetes = true
      });
      MockPack.EnablePodmanKubernetesDriver();
      MockPack.SetupKubePlay("test.yaml");
      MockPack.SetupKubeDown();

      var config = new KubePlayConfig { YamlPath = "test.yaml" };
      var resource = new PodmanKubernetesResource(Kernel, config);

      await resource.InitializeAsync();
      Assert.True(resource.IsInitialized);
      Assert.Equal("test.yaml", resource.YamlPath);
      Assert.NotNull(resource.PlayResult);
      Assert.Single(resource.Pods);

      await resource.DisposeAsync();
      Assert.False(resource.IsInitialized);
    }

    [Fact]
    public async Task PreflightAsync_FailsWithoutSupportsKubernetes()
    {
      MockPack.SetCapabilities(new DriverCapabilities
      {
        SupportsContainers = true,
        SupportsKubernetes = false
      });

      var config = new KubePlayConfig { YamlPath = "test.yaml" };
      var resource = new PodmanKubernetesResource(Kernel, config);

      await Assert.ThrowsAsync<CapabilityNotSupportedException>(
          () => resource.InitializeAsync());
    }

    [Fact]
    public async Task PreflightAsync_FailsWithoutIPodmanKubernetesDriver()
    {
      MockPack.SetCapabilities(new DriverCapabilities
      {
        SupportsContainers = true,
        SupportsKubernetes = true
      });
      // Don't call EnablePodmanKubernetesDriver()

      var config = new KubePlayConfig { YamlPath = "test.yaml" };
      var resource = new PodmanKubernetesResource(Kernel, config);

      await Assert.ThrowsAsync<InterfaceNotSupportedException>(
          () => resource.InitializeAsync());
    }

    [Fact]
    public async Task Play_Failure_ThrowsFluentDockerException()
    {
      MockPack.SetCapabilities(new DriverCapabilities
      {
        SupportsContainers = true,
        SupportsKubernetes = true
      });
      MockPack.EnablePodmanKubernetesDriver();

      MockPack.PodmanKubernetesDriver
          .Setup(d => d.PlayAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<KubePlayConfig>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<KubePlayResult>.Fail("play failed"));

      var config = new KubePlayConfig { YamlPath = "bad.yaml" };
      var resource = new PodmanKubernetesResource(Kernel, config);

      var ex = await Assert.ThrowsAsync<FluentDockerException>(
          () => resource.InitializeAsync());
      Assert.Contains("bad.yaml", ex.Message);
    }

    [Fact]
    public async Task GenerateYamlAsync_DelegatesToDriver()
    {
      MockPack.SetCapabilities(new DriverCapabilities
      {
        SupportsContainers = true,
        SupportsKubernetes = true
      });
      MockPack.EnablePodmanKubernetesDriver();
      MockPack.SetupKubePlay();
      MockPack.SetupKubeDown();
      MockPack.SetupKubeGenerate("apiVersion: v1\nkind: Pod\nmetadata:\n  name: test");

      var config = new KubePlayConfig { YamlPath = "test.yaml" };
      var resource = new PodmanKubernetesResource(Kernel, config);
      await resource.InitializeAsync();

      var yaml = await resource.GenerateYamlAsync("my-pod");

      Assert.Contains("kind: Pod", yaml);

      await resource.DisposeAsync();
    }

    [Fact]
    public async Task AccessBeforeInit_ThrowsInvalidOperationException()
    {
      var config = new KubePlayConfig { YamlPath = "test.yaml" };
      var resource = new PodmanKubernetesResource(Kernel, config);

      await Assert.ThrowsAsync<InvalidOperationException>(
          () => resource.GenerateYamlAsync("test"));
    }

    [Fact]
    public async Task GenerateYamlAsync_Failure_ThrowsFluentDockerException()
    {
      MockPack.SetCapabilities(new DriverCapabilities
      {
        SupportsContainers = true,
        SupportsKubernetes = true
      });
      MockPack.EnablePodmanKubernetesDriver();
      MockPack.SetupKubePlay();
      MockPack.SetupKubeDown();

      MockPack.PodmanKubernetesDriver
          .Setup(d => d.GenerateAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<string>.Fail("generate failed"));

      var config = new KubePlayConfig { YamlPath = "test.yaml" };
      var resource = new PodmanKubernetesResource(Kernel, config);
      await resource.InitializeAsync();

      var ex = await Assert.ThrowsAsync<FluentDockerException>(
          () => resource.GenerateYamlAsync("my-pod"));
      Assert.Contains("generate failed", ex.Message);

      await resource.DisposeAsync();
    }

    [Fact]
    public async Task TeardownAsync_DownFailure_ForceRemoveHandlesIt()
    {
      MockPack.SetCapabilities(new DriverCapabilities
      {
        SupportsContainers = true,
        SupportsKubernetes = true
      });
      MockPack.EnablePodmanKubernetesDriver();
      MockPack.SetupKubePlay();

      // DownAsync returns failure — triggers ForceRemoveAsync path
      MockPack.PodmanKubernetesDriver
          .Setup(d => d.DownAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Fail("down failed"));

      var config = new KubePlayConfig { YamlPath = "test.yaml" };
      var resource = new PodmanKubernetesResource(Kernel, config,
          new DockerResourceOptions { ForceRemoveOnDispose = true });
      await resource.InitializeAsync();

      // DisposeAsync should not throw — ForceRemoveAsync is best-effort
      await resource.DisposeAsync();
      Assert.False(resource.IsInitialized);
    }

    [Fact]
    public void Constructor_NullKernel_Throws()
    {
      Assert.Throws<ArgumentNullException>(
          () => new PodmanKubernetesResource(null, new KubePlayConfig { YamlPath = "x" }));
    }

    [Fact]
    public void Constructor_NullConfig_Throws()
    {
      Assert.Throws<ArgumentNullException>(
          () => new PodmanKubernetesResource(Kernel, null));
    }

    [Fact]
    public void Constructor_EmptyYamlPath_ThrowsArgumentException()
    {
      Assert.Throws<ArgumentException>(
          () => new PodmanKubernetesResource(Kernel, new KubePlayConfig { YamlPath = "" }));
    }

    [Fact]
    public void Constructor_NullYamlPath_ThrowsArgumentException()
    {
      Assert.Throws<ArgumentException>(
          () => new PodmanKubernetesResource(Kernel, new KubePlayConfig()));
    }

    [Fact]
    public async Task DisposeAsync_BeforeInit_DoesNotThrow()
    {
      var config = new KubePlayConfig { YamlPath = "never-init.yaml" };
      var resource = new PodmanKubernetesResource(Kernel, config);

      // Should not throw — teardown guards against uninitialized state
      await resource.DisposeAsync();
      Assert.False(resource.IsInitialized);
    }
  }
}
