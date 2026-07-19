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
    public async Task DisposeAsync_StopFails_ForceRemoveDownSucceeds()
    {
      MockPack.SetupComposeUpAsync(new FluentDocker.Drivers.ComposeUpResult
      {
        ProjectName = "test-project"
      });
      MockPack.SetupComposeList();
      MockPack.SetupComposeStart();
      MockPack.SetupComposeStopFailure();
      MockPack.SetupComposeDown();

      var resource = new ComposeResource(
          Kernel,
          builder => builder.WithComposeFile("/path/to/docker-compose.yml"),
          new DockerResourceOptions { ForceRemoveOnDispose = true });

      await resource.InitializeAsync(TestContext.Current.CancellationToken);
      await resource.DisposeAsync();

      Assert.False(resource.IsInitialized);
      Assert.NotNull(resource.LastTeardownDiagnostics);
      Assert.Null(resource.LastTeardownDiagnostics.ForceRemoveException);
    }

    [Fact]
    public async Task DisposeAsync_StopAndForceRemoveFail_CapturesBothFailures()
    {
      MockPack.SetupComposeUpAsync(new FluentDocker.Drivers.ComposeUpResult
      {
        ProjectName = "test-project"
      });
      MockPack.SetupComposeList();
      MockPack.SetupComposeStart();
      MockPack.SetupComposeStopFailure("stop failed");
      MockPack.SetupComposeDownFailure("down failed");

      var resource = new ComposeResource(
          Kernel,
          builder => builder.WithComposeFile("/path/to/docker-compose.yml"),
          new DockerResourceOptions { ForceRemoveOnDispose = true });

      await resource.InitializeAsync(TestContext.Current.CancellationToken);
      var ex = await Assert.ThrowsAsync<FluentDocker.Common.DriverException>(
          () => resource.DisposeAsync().AsTask());

      Assert.Contains("stop failed", ex.Message);
      Assert.NotNull(resource.LastTeardownDiagnostics);
      Assert.NotNull(resource.LastTeardownDiagnostics.TeardownException);
      Assert.NotNull(resource.LastTeardownDiagnostics.ForceRemoveException);
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

      var ex = await Assert.ThrowsAsync<ResourceInitializationException>(
          () => resource.InitializeAsync(TestContext.Current.CancellationToken));
      Assert.IsType<FluentDocker.Common.CapabilityNotSupportedException>(ex.InnerException);
    }

    [Fact]
    public void Constructor_NullKernel_Throws()
    {
      Assert.Throws<ArgumentNullException>(
          () => new ComposeResource(null!, _ => { }));
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
