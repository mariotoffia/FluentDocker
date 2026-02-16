using System;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Kernel;
using FluentDocker.Testing.Core;
using FluentDocker.Testing.Xunit;
using FluentDocker.Tests.Mocks;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Testing.Adapters
{
  [Trait("Category", "Unit")]
  public class XunitComposeFixtureTests : MockKernelTestBase, IAsyncLifetime
  {
    public async ValueTask InitializeAsync()
    {
      await InitializeMockKernelAsync();
    }

    [Fact]
    public void PropertiesBeforeInit_ReturnNull()
    {
      var fixture = new XunitComposeFixture();
      Assert.Null(fixture.Resource);
      Assert.Null(fixture.Service);
      Assert.Null(fixture.Kernel);
    }

    [Fact]
    public async Task InitializeAsync_WithCustomKernel_UsesProvidedKernel()
    {
      MockPack.SetupComposeUpAsync(new FluentDocker.Drivers.ComposeUpResult
      {
        ProjectName = "fixture-compose"
      });
      MockPack.SetupComposeStart();
      MockPack.SetupComposeStop();
      MockPack.SetupComposeDown();

      var fixture = new XunitComposeFixture();

      await fixture.InitializeAsync(
          configure: c => c.WithComposeFile("/path/docker-compose.yml")
              .WithProjectName("fixture-compose"),
          kernelFactory: () => Task.FromResult(Kernel),
          cancellationToken: TestContext.Current.CancellationToken);

      Assert.NotNull(fixture.Resource);
      Assert.NotNull(fixture.Service);
      Assert.Same(Kernel, fixture.Kernel);
      Assert.True(fixture.Resource.IsInitialized);

      await fixture.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_CleansUpResourceAndKernel()
    {
      MockPack.SetupComposeUpAsync(new FluentDocker.Drivers.ComposeUpResult
      {
        ProjectName = "dispose-compose"
      });
      MockPack.SetupComposeStart();
      MockPack.SetupComposeStop();
      MockPack.SetupComposeDown();

      var fixture = new XunitComposeFixture();

      await fixture.InitializeAsync(
          configure: c => c.WithComposeFile("/path/docker-compose.yml"),
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
      var fixture = new XunitComposeFixture();
      await fixture.DisposeAsync();
    }

    [Fact]
    public async Task InitializeAsync_CalledTwice_ThrowsInvalidOperationException()
    {
      MockPack.SetupComposeUpAsync(new FluentDocker.Drivers.ComposeUpResult
      {
        ProjectName = "twice-compose"
      });
      MockPack.SetupComposeStart();
      MockPack.SetupComposeStop();
      MockPack.SetupComposeDown();

      var fixture = new XunitComposeFixture();

      await fixture.InitializeAsync(
          configure: c => c.WithComposeFile("/path/docker-compose.yml"),
          kernelFactory: () => Task.FromResult(Kernel),
          cancellationToken: TestContext.Current.CancellationToken);

      await Assert.ThrowsAsync<InvalidOperationException>(() =>
          fixture.InitializeAsync(
              configure: c => c.WithComposeFile("/path/docker-compose.yml"),
              kernelFactory: () => Task.FromResult(Kernel),
              cancellationToken: TestContext.Current.CancellationToken));

      await fixture.DisposeAsync();
    }
  }

  [Trait("Category", "Unit")]
  public class XunitTopologyFixtureTests : MockKernelTestBase, IAsyncLifetime
  {
    public async ValueTask InitializeAsync()
    {
      await InitializeMockKernelAsync();
    }

    [Fact]
    public void PropertiesBeforeInit_ReturnNull()
    {
      var fixture = new XunitTopologyFixture();
      Assert.Null(fixture.Resource);
      Assert.Null(fixture.Kernel);
    }

    [Fact]
    public async Task InitializeAsync_WithCustomKernel_UsesProvidedKernel()
    {
      MockPack
          .SetupContainerCreate()
          .SetupContainerStart()
          .SetupContainerInspect(running: true)
          .SetupContainerStop()
          .SetupContainerRemove();

      var fixture = new XunitTopologyFixture();

      await fixture.InitializeAsync(
          configure: b => b.UseContainer(c => c.UseImage("alpine:latest")),
          kernelFactory: () => Task.FromResult(Kernel),
          cancellationToken: TestContext.Current.CancellationToken);

      Assert.NotNull(fixture.Resource);
      Assert.Same(Kernel, fixture.Kernel);
      Assert.True(fixture.Resource.IsInitialized);
      Assert.NotEmpty(fixture.Resource.Services);

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

      var fixture = new XunitTopologyFixture();

      await fixture.InitializeAsync(
          configure: b => b.UseContainer(c => c.UseImage("alpine:latest")),
          kernelFactory: () => Task.FromResult(Kernel),
          cancellationToken: TestContext.Current.CancellationToken);

      await fixture.DisposeAsync();
      Assert.Null(fixture.Resource);
      Assert.Null(fixture.Kernel);
    }

    [Fact]
    public async Task DisposeAsync_BeforeInit_DoesNotThrow()
    {
      var fixture = new XunitTopologyFixture();
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

      var fixture = new XunitTopologyFixture();

      await fixture.InitializeAsync(
          configure: b => b.UseContainer(c => c.UseImage("alpine:latest")),
          kernelFactory: () => Task.FromResult(Kernel),
          cancellationToken: TestContext.Current.CancellationToken);

      await Assert.ThrowsAsync<InvalidOperationException>(() =>
          fixture.InitializeAsync(
              configure: b => b.UseContainer(c => c.UseImage("nginx:latest")),
              kernelFactory: () => Task.FromResult(Kernel),
              cancellationToken: TestContext.Current.CancellationToken));

      await fixture.DisposeAsync();
    }
  }
}
