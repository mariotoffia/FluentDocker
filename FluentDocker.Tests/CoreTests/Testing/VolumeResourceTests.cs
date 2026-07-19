using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Model.Drivers;
using FluentDocker.Testing.Core;
using FluentDocker.Tests.Mocks;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Testing
{
  [Trait("Category", "Unit")]
  public class VolumeResourceTests : MockKernelTestBase, IAsyncLifetime
  {
    public async ValueTask InitializeAsync()
    {
      await InitializeMockKernelAsync();
    }

    [Fact]
    public async Task InitializeAsync_CreatesVolume()
    {
      MockPack
          .SetupVolumeCreate("my-volume")
          .SetupVolumeRemove();

      var resource = new VolumeResource(
          Kernel,
          config => config.Name = "my-volume");

      await resource.InitializeAsync(TestContext.Current.CancellationToken);

      Assert.True(resource.IsInitialized);
      Assert.Equal("my-volume", resource.VolumeName);
    }

    [Fact]
    public async Task DisposeAsync_RemovesVolume()
    {
      MockPack
          .SetupVolumeCreate()
          .SetupVolumeRemove();

      var resource = new VolumeResource(
          Kernel,
          config => config.Name = "test-volume");

      await resource.InitializeAsync(TestContext.Current.CancellationToken);
      await resource.DisposeAsync();

      Assert.False(resource.IsInitialized);
    }

    [Fact]
    public async Task InitializeAsync_WithCustomConfig_PassesConfig()
    {
      MockPack
          .SetupVolumeCreate()
          .SetupVolumeRemove();

      var resource = new VolumeResource(
          Kernel,
          config =>
          {
            config.Name = "custom-vol";
            config.Driver = "local";
          });

      await resource.InitializeAsync(TestContext.Current.CancellationToken);
      Assert.True(resource.IsInitialized);
    }

    [Fact]
    public async Task InitializeAsync_NoName_GeneratesUniqueName()
    {
      MockPack
          .SetupVolumeCreate()
          .SetupVolumeRemove();

      var resource = new VolumeResource(
          Kernel,
          config => { /* no name set */ });

      await resource.InitializeAsync(TestContext.Current.CancellationToken);
      Assert.True(resource.IsInitialized);
      Assert.StartsWith("vol-", resource.VolumeName);
    }

    [Fact]
    public async Task InitializeAsync_WhenDriverIsUnhealthy_UsesUnavailableSentinel()
    {
      MockPack.SetHealthy(false);
      var resource = new VolumeResource(
          Kernel,
          config => config.Name = "unhealthy-volume");

      var ex = await Assert.ThrowsAsync<ResourceInitializationException>(
          () => resource.InitializeAsync(TestContext.Current.CancellationToken));

      Assert.IsType<FluentDockerUnavailableException>(ex.InnerException);
      Assert.Contains("Is Docker running?", ex.InnerException.Message);
    }

    [Fact]
    public void Constructor_NullConfigure_Throws()
    {
      Assert.Throws<ArgumentNullException>(() =>
          new VolumeResource(Kernel, null!));
    }

    [Fact]
    public async Task DisposeAsync_GracefulRemoveFails_PropagatesInsteadOfSwallowing()
    {
      // The volume driver returns CommandResponse.Fail (it does not throw) when a
      // volume is still in use. TeardownAsync must surface that instead of reporting
      // a clean teardown — otherwise the resource leaks silently.
      MockPack.SetupVolumeCreate("vol-stuck");
      MockPack.VolumeDriver
          .Setup(d => d.RemoveAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              It.IsAny<bool>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Fail(
              "volume is in use", ErrorCodes.Volume.InUse));

      var resource = new VolumeResource(
          Kernel,
          config => config.Name = "stuck-volume",
          new DockerResourceOptions { ForceRemoveOnDispose = false });

      await resource.InitializeAsync(TestContext.Current.CancellationToken);

      await Assert.ThrowsAsync<DriverException>(
          () => resource.DisposeAsync().AsTask());
    }
  }
}
