using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Containers;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Models;
using FluentDocker.Model.Models.Options;
using FluentDocker.Testing.Core;
using FluentDocker.Testing.NUnit;
using FluentDocker.Tests.Mocks;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Testing.Adapters
{
  [Trait("Category", "Unit")]
  public class NUnitAdapterTests : MockKernelTestBase, IAsyncLifetime
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

      var (kernel, resource) = await NUnitResourceHelpers.CreateContainerAsync(
          configure: c => c.UseImage("redis:alpine"),
          kernelFactory: () => Task.FromResult(Kernel),
          cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(resource.IsInitialized);
      Assert.Same(Kernel, kernel);

      await NUnitResourceHelpers.DisposeAsync(resource, null!);
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
          kernelFactory: () => Task.FromResult(Kernel),
          cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(resource.IsInitialized);
      Assert.Equal("nunit-stack", resource.StackName);
      Assert.Same(Kernel, kernel);

      await NUnitResourceHelpers.DisposeAsync(resource, null!);
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
          kernelFactory: () => Task.FromResult(Kernel),
          cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(resource.IsInitialized);
      Assert.Equal("nunit.yaml", resource.YamlPath);
      Assert.Same(Kernel, kernel);

      await NUnitResourceHelpers.DisposeAsync(resource, null!);
    }

    [Fact]
    public async Task CreateComposeAsync_WithCustomKernel_ReturnsInitializedResource()
    {
      MockPack.SetupComposeUpAsync(new FluentDocker.Drivers.ComposeUpResult
      {
        ProjectName = "nunit-compose"
      });
      MockPack.SetupComposeStart();
      MockPack.SetupComposeStop();
      MockPack.SetupComposeDown();

      var (kernel, resource) = await NUnitResourceHelpers.CreateComposeAsync(
          configure: c => c.WithComposeFile("/path/docker-compose.yml")
              .WithProjectName("nunit-compose"),
          kernelFactory: () => Task.FromResult(Kernel),
          cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(resource.IsInitialized);
      Assert.NotNull(resource.Service);
      Assert.Same(Kernel, kernel);

      await NUnitResourceHelpers.DisposeAsync(resource, null!);
    }

    [Fact]
    public async Task CreateTopologyAsync_WithCustomKernel_ReturnsInitializedResource()
    {
      MockPack
          .SetupContainerCreate()
          .SetupContainerStart()
          .SetupContainerInspect(running: true)
          .SetupContainerStop()
          .SetupContainerRemove();

      var (kernel, resource) = await NUnitResourceHelpers.CreateTopologyAsync(
          configure: b => b.UseContainer(c => c.UseImage("alpine:latest")),
          kernelFactory: () => Task.FromResult(Kernel),
          cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(resource.IsInitialized);
      Assert.NotEmpty(resource.Services);
      Assert.Same(Kernel, kernel);

      await NUnitResourceHelpers.DisposeAsync(resource, null!);
    }

    [Fact]
    public async Task DisposeAsync_NullResourceAndKernel_DoesNotThrow()
    {
      await NUnitResourceHelpers.DisposeAsync(null!, null!);
    }

    [Fact]
    public async Task DisposeAsync_NullResource_DisposesKernel()
    {
      var (kernel, _) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("dispose-test");

      await NUnitResourceHelpers.DisposeAsync(null, kernel);
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

      await Assert.ThrowsAsync<ResourceInitializationException>(() =>
          NUnitResourceHelpers.CreateContainerAsync(
              configure: c => c.UseImage("fail:image"),
              kernelFactory: () => Task.FromResult(testKernel),
              cancellationToken: TestContext.Current.CancellationToken));
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

      var (kernel, resource) = await NUnitResourceHelpers.CreateResourceAsync<ContainerResource>(
          k => new ContainerResource(k, c => c.UseImage("alpine:latest")),
          kernelFactory: () => Task.FromResult(Kernel),
          cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(resource.IsInitialized);
      Assert.Same(Kernel, kernel);

      await NUnitResourceHelpers.DisposeAsync(resource, null!);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CreateResourceAsync_ModelResource_LoadsAndUnloadsModel()
    {
      var model = ModelReference.Parse("ai/smollm2:latest");
      MockPack
          .SetupModelLoad()
          .SetupModelUnload()
          .EnableModelDrivers();

      var (kernel, resource) = await NUnitResourceHelpers.CreateResourceAsync<ModelResource>(
          k => new ModelResource(k, model),
          kernelFactory: () => Task.FromResult(Kernel),
          cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(resource.IsInitialized);
      Assert.Same(resource.Service.Runner, resource.Runner);
      Assert.Same(Kernel, kernel);
      MockPack.ModelRuntimeDriver.Verify(d => d.LoadAsync(
          It.IsAny<DriverContext>(),
          It.Is<ModelReference>(m => m.Equals(model)),
          It.IsAny<ModelRunOptions>(),
          It.IsAny<CancellationToken>()), Times.Once);

      await NUnitResourceHelpers.DisposeAsync(resource, null!);

      MockPack.ModelRuntimeDriver.Verify(d => d.UnloadAsync(
          It.IsAny<DriverContext>(),
          It.Is<ModelReference>(m => m.Equals(model)),
          It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateResourceAsync_NullFactory_ThrowsArgumentNullException()
    {
      await Assert.ThrowsAsync<ArgumentNullException>(() =>
          NUnitResourceHelpers.CreateResourceAsync<ContainerResource>(null!,
              cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CreateResourceAsync_FactoryReturnsNull_ThrowsInvalidOperationException()
    {
      var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
          NUnitResourceHelpers.CreateResourceAsync<ContainerResource>(
              _ => null!,
              kernelFactory: () => Task.FromResult(Kernel),
              cancellationToken: TestContext.Current.CancellationToken));

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

      await Assert.ThrowsAsync<ResourceInitializationException>(() =>
          NUnitResourceHelpers.CreateResourceAsync<ContainerResource>(
              k => new ContainerResource(k, c => c.UseImage("fail:img")),
              kernelFactory: () => Task.FromResult(testKernel),
              cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ContainerFixture_SetUpAsyncCalledTwice_IsNoOp()
    {
      MockPack
              .SetupContainerCreate()
              .SetupContainerStart()
              .SetupContainerInspect(running: true)
              .SetupContainerStop()
              .SetupContainerRemove();

      var fixture = new TestNUnitContainerFixture(Kernel);

      await fixture.SetUpAsync();
      await fixture.SetUpAsync();

      Assert.True(fixture.Resource.IsInitialized);
      MockPack.VerifyContainerCreated("alpine:latest", Times.Once());

      await fixture.TearDownAsync();
    }

    private sealed class TestNUnitContainerFixture(FluentDockerKernel kernel)
        : NUnitContainerFixtureBase
    {
      private readonly FluentDockerKernel _kernel = kernel;

      protected override Func<Task<FluentDockerKernel>>? KernelFactory =>
              () => Task.FromResult(_kernel);

      protected override void ConfigureContainer(IContainerBuilder builder)
      {
        builder.UseImage("alpine:latest");
      }
    }
  }
}
