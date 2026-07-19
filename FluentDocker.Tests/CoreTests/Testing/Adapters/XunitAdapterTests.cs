using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Podman;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Models;
using FluentDocker.Model.Models.Options;
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
    public void PropertiesBeforeInit_ThrowInvalidOperationException()
    {
      var fixture = new XunitContainerFixture();
      Assert.Throws<InvalidOperationException>(() => _ = fixture.Resource);
      Assert.Throws<InvalidOperationException>(() => _ = fixture.Container);
      Assert.Throws<InvalidOperationException>(() => _ = fixture.Kernel);
    }

    [Fact]
    public async Task InitializeAsync_WithCustomKernelFactory_UsesProvidedKernel()
    {
      var (ownedKernel, ownedPack) =
          await MockKernelBuilderExtensions.CreateWithMockDriverAsync("xunit-owned");
      ownedPack
          .SetupContainerCreate()
          .SetupContainerStart()
          .SetupContainerInspect(running: true)
          .SetupContainerStop()
          .SetupContainerRemove();

      var fixture = new XunitContainerFixture();

      await fixture.InitializeAsync(
          configure: c => c.UseImage("redis:alpine"),
          kernelFactory: () => Task.FromResult(ownedKernel),
          cancellationToken: TestContext.Current.CancellationToken);

      Assert.NotNull(fixture.Resource);
      Assert.Same(ownedKernel, fixture.Kernel);
      Assert.True(fixture.Resource.IsInitialized);

      await fixture.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_CleansUpResourceAndKernel()
    {
      var (ownedKernel, ownedPack) =
          await MockKernelBuilderExtensions.CreateWithMockDriverAsync("xunit-dispose");
      ownedPack
          .SetupContainerCreate()
          .SetupContainerStart()
          .SetupContainerInspect(running: true)
          .SetupContainerStop()
          .SetupContainerRemove();

      var fixture = new XunitContainerFixture();

      await fixture.InitializeAsync(
          configure: c => c.UseImage("alpine:latest"),
          kernelFactory: () => Task.FromResult(ownedKernel),
          cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(fixture.Resource.IsInitialized);

      await fixture.DisposeAsync();
      Assert.Throws<InvalidOperationException>(() => _ = fixture.Resource);
      Assert.Throws<InvalidOperationException>(() => _ = fixture.Kernel);
    }

    [Fact]
    public async Task DisposeAsync_BeforeInit_DoesNotThrow()
    {
      var fixture = new XunitContainerFixture();
      // Should not throw even when never initialized
      await fixture.DisposeAsync();
    }

    [Fact]
    public async Task ConditionalFixture_WhenDockerUnavailable_MarksSkippedWithoutProvisioning()
    {
      var pack = new MockDriverPack();
      pack.SetHealthy(false);
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("xunit-unavailable", pack);
      var fixture = new UnavailableConditionalFixture(() => Task.FromResult(kernel));

      await fixture.InitializeAsync();

      Assert.True(fixture.IsSkipped);
      Assert.Contains("not reachable", fixture.SkipReason);
      Assert.Throws<InvalidOperationException>(() => _ = fixture.Resource);
      pack.ContainerDriver.Verify(
          d => d.CreateAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<ContainerCreateConfig>(),
              It.IsAny<CancellationToken>()),
          Times.Never);
      await fixture.DisposeAsync();
    }

    [Fact]
    public async Task ConditionalFixture_WrappedUnavailableDuringProvision_SkipsInsteadOfThrowing()
    {
      // TSTX-3: the daemon can die BETWEEN the health probe and resource init — the
      // unavailability then arrives wrapped in ResourceInitializationException. The fixture
      // must convert that to a skip (like the MSTest/NUnit adapters), not error the class.
      var pack = new MockDriverPack();
      pack.SetHealthy(true);
      pack.ContainerDriver
          .Setup(d => d.CreateAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<ContainerCreateConfig>(),
              It.IsAny<CancellationToken>()))
          .ThrowsAsync(new FluentDockerUnavailableException("daemon went away mid-init"));
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("xunit-wrapped", pack);
      var fixture = new UnavailableConditionalFixture(() => Task.FromResult(kernel));

      await fixture.InitializeAsync();

      Assert.True(fixture.IsSkipped);
      Assert.Contains("daemon went away mid-init", fixture.SkipReason);
      Assert.Throws<InvalidOperationException>(() => _ = fixture.Resource);
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

      await Assert.ThrowsAsync<ResourceInitializationException>(() =>
          fixture.InitializeAsync(
              configure: c => c.UseImage("fail:image"),
              kernelFactory: () => Task.FromResult(testKernel),
              cancellationToken: TestContext.Current.CancellationToken));

      // After failure, fixture state should be clean (getters throw)
      Assert.Throws<InvalidOperationException>(() => _ = fixture.Resource);
      Assert.Throws<InvalidOperationException>(() => _ = fixture.Kernel);
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
      Assert.Throws<InvalidOperationException>(() => _ = fixture.Resource);
      Assert.Throws<InvalidOperationException>(() => _ = fixture.Kernel);

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

    private sealed class UnavailableConditionalFixture(
        Func<Task<FluentDockerKernel>> kernelFactory) : XunitConditionalContainerFixtureBase
    {
      protected override Func<Task<FluentDockerKernel>>? KernelFactory => kernelFactory;

      protected override void ConfigureContainer(IContainerBuilder builder)
      {
        builder.UseImage("alpine:latest");
      }
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
    [Trait("Category", "Unit")]
    public async Task InitializeAsync_ModelResource_LoadsAndUnloadsModel()
    {
      var model = ModelReference.Parse("ai/smollm2:latest");
      MockPack
          .SetupModelLoad()
          .SetupModelUnload()
          .EnableModelDrivers();

      var fixture = new XunitResourceFixture<ModelResource>();

      await fixture.InitializeAsync(
          kernel => new ModelResource(kernel, model),
          kernelFactory: () => Task.FromResult(Kernel),
          cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(fixture.Resource.IsInitialized);
      Assert.Same(fixture.Resource.Service.Runner, fixture.Resource.Runner);
      MockPack.ModelRuntimeDriver.Verify(d => d.LoadAsync(
          It.IsAny<DriverContext>(),
          It.Is<ModelReference>(m => m.Equals(model)),
          It.IsAny<ModelRunOptions>(),
          It.IsAny<CancellationToken>()), Times.Once);

      await fixture.DisposeAsync();

      MockPack.ModelRuntimeDriver.Verify(d => d.UnloadAsync(
          It.IsAny<DriverContext>(),
          It.Is<ModelReference>(m => m.Equals(model)),
          It.IsAny<CancellationToken>()), Times.Once);
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
      Assert.Throws<InvalidOperationException>(() => _ = fixture.Resource);

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

      await Assert.ThrowsAsync<ResourceInitializationException>(() =>
          fixture.InitializeAsync(
              kernel => new ContainerResource(kernel, c => c.UseImage("fail:img")),
              kernelFactory: () => Task.FromResult(testKernel),
              cancellationToken: TestContext.Current.CancellationToken));

      Assert.Throws<InvalidOperationException>(() => _ = fixture.Resource);
      Assert.Throws<InvalidOperationException>(() => _ = fixture.Kernel);
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
    public void PropertiesBeforeInit_ThrowInvalidOperationException()
    {
      var fixture = new XunitSwarmStackFixture();
      Assert.Throws<InvalidOperationException>(() => _ = fixture.Resource);
      Assert.Throws<InvalidOperationException>(() => _ = fixture.StackName);
      Assert.Throws<InvalidOperationException>(() => _ = fixture.Kernel);
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
      Assert.StartsWith("fixture-stack", fixture.StackName); // session-scoped by default

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
    public void PropertiesBeforeInit_ThrowInvalidOperationException()
    {
      var fixture = new XunitPodmanKubernetesFixture();
      Assert.Throws<InvalidOperationException>(() => _ = fixture.Resource);
      Assert.Throws<InvalidOperationException>(() => _ = fixture.YamlPath);
      Assert.Throws<InvalidOperationException>(() => _ = fixture.Kernel);
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
