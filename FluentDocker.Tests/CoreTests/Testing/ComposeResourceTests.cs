using System;
using System.Threading.Tasks;
using FluentDocker.Model.Drivers;
using FluentDocker.Testing.Core;
using FluentDocker.Tests.Mocks;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Testing
{
  [Trait("Category", "Unit")]
  public class ComposeResourceTests : MockKernelTestBase, IAsyncLifetime
  {
    public async ValueTask InitializeAsync()
    {
      await InitializeMockKernelAsync();
    }

    [Fact]
    public async Task InitializeAsync_CreatesComposeService()
    {
      MockPack.SetupComposeUpAsync(new FluentDocker.Drivers.ComposeUpResult
      {
        ProjectName = "test-project",
        Services = ["web", "db"]
      });
      MockPack.SetupComposeStart();

      var resource = new ComposeResource(
          Kernel,
          builder => builder
              .WithComposeFile("/path/to/docker-compose.yml")
              .WithProjectName("test-project"));

      await resource.InitializeAsync(TestContext.Current.CancellationToken);

      Assert.True(resource.IsInitialized);
      Assert.NotNull(resource.Service);
    }

    [Fact]
    public async Task DisposeAsync_StopsAndRemovesService()
    {
      MockPack.SetupComposeUpAsync(new FluentDocker.Drivers.ComposeUpResult
      {
        ProjectName = "test-project"
      });
      MockPack.SetupComposeStart();
      MockPack.SetupComposeStop();
      MockPack.SetupComposeDown();

      var resource = new ComposeResource(
          Kernel,
          builder => builder.WithComposeFile("/path/to/docker-compose.yml"));

      await resource.InitializeAsync(TestContext.Current.CancellationToken);
      await resource.DisposeAsync();

      Assert.False(resource.IsInitialized);
    }

    [Fact]
    public async Task PreflightAsync_FailsWhenComposeNotSupported()
    {
      MockPack.SetCapabilities(new DriverCapabilities
      {
        SupportsCompose = false,
        SupportsContainers = true
      });

      var resource = new ComposeResource(
          Kernel,
          builder => builder.WithComposeFile("/path/to/docker-compose.yml"));

      await Assert.ThrowsAsync<FluentDocker.Common.CapabilityNotSupportedException>(
          () => resource.InitializeAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Constructor_NullKernel_Throws()
    {
      Assert.Throws<ArgumentNullException>(
          () => new ComposeResource(null, _ => { }));
    }

    [Fact]
    public void Constructor_NullConfigure_Throws()
    {
      Assert.Throws<ArgumentNullException>(
          () => new ComposeResource(Kernel, null!));
    }

    [Fact]
    public async Task GetLogsAsync_BeforeInit_Throws()
    {
      var resource = new ComposeResource(
          Kernel,
          builder => builder.WithComposeFile("/path/to/docker-compose.yml"));

      await Assert.ThrowsAsync<InvalidOperationException>(
          () => resource.GetLogsAsync(TestContext.Current.CancellationToken));
    }
  }
}
