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
    public async ValueTask InitializeAsync()
    {
      await InitializeMockKernelAsync();
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
          kernelFactory: () => Task.FromResult(capturedKernel),
          cancellationToken: TestContext.Current.CancellationToken);

      Assert.NotNull(fixture.Resource);
      Assert.Same(capturedKernel, fixture.Kernel);
      Assert.True(fixture.Resource.IsInitialized);

      await fixture.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_CleansUpResourceAndKernel()
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
          kernelFactory: () => Task.FromResult(Kernel),
          cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(fixture.Resource.IsInitialized);

      await fixture.DisposeAsync();
      Assert.Null(fixture.Resource);
      Assert.Null(fixture.Kernel);
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
          kernelFactory: () => Task.FromResult(Kernel),
          cancellationToken: TestContext.Current.CancellationToken);

      await Assert.ThrowsAsync<InvalidOperationException>(() =>
          fixture.InitializeAsync(
              configure: c => c.UseImage("redis:alpine"),
              kernelFactory: () => Task.FromResult(Kernel),
              cancellationToken: TestContext.Current.CancellationToken));

      await fixture.DisposeAsync();
    }

    [Fact]
    public async Task InitializeAsync_WhenResourceInitFails_CleansUpKernel()
    {
      var (testKernel, testPack) =
          await MockKernelBuilderExtensions.CreateWithMockDriverAsync("fail-test");

      // CreateAsync throws — simulates provisioning failure
      testPack.ContainerDriver
          .Setup(d => d.CreateAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<ContainerCreateConfig>(),
              It.IsAny<CancellationToken>()))
          .ThrowsAsync(new InvalidOperationException("create failed"));

      var fixture = new XunitContainerFixture();

      await Assert.ThrowsAsync<InvalidOperationException>(() =>
          fixture.InitializeAsync(
              configure: c => c.UseImage("fail:image"),
              kernelFactory: () => Task.FromResult(testKernel),
              cancellationToken: TestContext.Current.CancellationToken));

      // After failure, fixture state should be clean
      Assert.Null(fixture.Resource);
      Assert.Null(fixture.Kernel);
    }

    [Fact]
    public async Task InitializeAsync_AfterDispose_CanReinitialize()
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
          kernelFactory: () => Task.FromResult(Kernel),
          cancellationToken: TestContext.Current.CancellationToken);
      Assert.NotNull(fixture.Resource);

      await fixture.DisposeAsync();
      Assert.Null(fixture.Resource);
      Assert.Null(fixture.Kernel);

      // Create a new kernel for re-init since old one was disposed
      var (newKernel, newPack) =
          await MockKernelBuilderExtensions.CreateWithMockDriverAsync("reinit");
      newPack
          .SetupContainerCreate("reinit-container")
          .SetupContainerStart()
          .SetupContainerInspect(containerId: "reinit-container", running: true)
          .SetupContainerStop()
          .SetupContainerRemove();

      await fixture.InitializeAsync(
          configure: c => c.UseImage("nginx:alpine"),
          kernelFactory: () => Task.FromResult(newKernel),
          cancellationToken: TestContext.Current.CancellationToken);

      Assert.NotNull(fixture.Resource);
      Assert.True(fixture.Resource.IsInitialized);

      await fixture.DisposeAsync();
    }
  }

  [Trait("Category", "Unit")]
  public class XunitResourceFixtureGenericTests : MockKernelTestBase, IAsyncLifetime
  {
    public async ValueTask InitializeAsync()
    {
      await InitializeMockKernelAsync();
    }

    [Fact]
    public async Task InitializeAsync_SuccessPath_CreatesResource()
    {
      MockPack
          .SetupContainerCreate()
          .SetupContainerStart()
          .SetupContainerInspect(running: true)
          .SetupContainerStop()
          .SetupContainerRemove();

      var fixture = new XunitResourceFixture<ContainerResource>();

      await fixture.InitializeAsync(
          kernel => new ContainerResource(kernel, c => c.UseImage("alpine:latest")),
          kernelFactory: () => Task.FromResult(Kernel),
          cancellationToken: TestContext.Current.CancellationToken);

      Assert.NotNull(fixture.Resource);
      Assert.True(fixture.Resource.IsInitialized);
      Assert.Same(Kernel, fixture.Kernel);

      await fixture.DisposeAsync();
    }

    [Fact]
    public async Task InitializeAsync_NullFactory_ThrowsArgumentNullException()
    {
      var fixture = new XunitResourceFixture<ContainerResource>();

      await Assert.ThrowsAsync<ArgumentNullException>(() =>
          fixture.InitializeAsync(null!,
              cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task InitializeAsync_FactoryReturnsNull_ThrowsInvalidOperationException()
    {
      var fixture = new XunitResourceFixture<ContainerResource>();

      var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
          fixture.InitializeAsync(
              _ => null!,
              kernelFactory: () => Task.FromResult(Kernel),
              cancellationToken: TestContext.Current.CancellationToken));

      Assert.Contains("resourceFactory returned null", ex.Message);
    }

    [Fact]
    public async Task InitializeAsync_AfterDispose_CanReinitialize()
    {
      MockPack
          .SetupContainerCreate()
          .SetupContainerStart()
          .SetupContainerInspect(running: true)
          .SetupContainerStop()
          .SetupContainerRemove();

      var fixture = new XunitResourceFixture<ContainerResource>();

      await fixture.InitializeAsync(
          kernel => new ContainerResource(kernel, c => c.UseImage("alpine:latest")),
          kernelFactory: () => Task.FromResult(Kernel),
          cancellationToken: TestContext.Current.CancellationToken);

      await fixture.DisposeAsync();
      Assert.Null(fixture.Resource);

      var (newKernel, newPack) =
          await MockKernelBuilderExtensions.CreateWithMockDriverAsync("reinit");
      newPack
          .SetupContainerCreate("reinit-ctr")
          .SetupContainerStart()
          .SetupContainerInspect(containerId: "reinit-ctr", running: true)
          .SetupContainerStop()
          .SetupContainerRemove();

      await fixture.InitializeAsync(
          kernel => new ContainerResource(kernel, c => c.UseImage("nginx:alpine")),
          kernelFactory: () => Task.FromResult(newKernel),
          cancellationToken: TestContext.Current.CancellationToken);

      Assert.NotNull(fixture.Resource);
      Assert.True(fixture.Resource.IsInitialized);

      await fixture.DisposeAsync();
    }

    [Fact]
    public async Task InitializeAsync_WhenResourceInitFails_CleansUp()
    {
      var (testKernel, testPack) =
          await MockKernelBuilderExtensions.CreateWithMockDriverAsync("fail-generic");

      testPack.ContainerDriver
          .Setup(d => d.CreateAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<ContainerCreateConfig>(),
              It.IsAny<CancellationToken>()))
          .ThrowsAsync(new InvalidOperationException("create failed"));

      var fixture = new XunitResourceFixture<ContainerResource>();

      await Assert.ThrowsAsync<InvalidOperationException>(() =>
          fixture.InitializeAsync(
              kernel => new ContainerResource(kernel, c => c.UseImage("fail:img")),
              kernelFactory: () => Task.FromResult(testKernel),
              cancellationToken: TestContext.Current.CancellationToken));

      Assert.Null(fixture.Resource);
      Assert.Null(fixture.Kernel);
    }
  }

  [Trait("Category", "Unit")]
  public class XunitSwarmStackFixtureTests : MockKernelTestBase, IAsyncLifetime
  {
    public async ValueTask InitializeAsync()
    {
      await InitializeMockKernelAsync();
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
          kernelFactory: () => Task.FromResult(capturedKernel),
          cancellationToken: TestContext.Current.CancellationToken);

      Assert.NotNull(fixture.Resource);
      Assert.Same(capturedKernel, fixture.Kernel);
      Assert.Equal("fixture-stack", fixture.StackName);

      await fixture.DisposeAsync();
    }
  }

  [Trait("Category", "Unit")]
  public class XunitPodmanKubernetesFixtureTests : MockKernelTestBase, IAsyncLifetime
  {
    public async ValueTask InitializeAsync()
    {
      await InitializeMockKernelAsync();
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
          kernelFactory: () => Task.FromResult(capturedKernel),
          cancellationToken: TestContext.Current.CancellationToken);

      Assert.NotNull(fixture.Resource);
      Assert.Same(capturedKernel, fixture.Kernel);
      Assert.Equal("fixture.yaml", fixture.YamlPath);

      await fixture.DisposeAsync();
    }
  }
}
