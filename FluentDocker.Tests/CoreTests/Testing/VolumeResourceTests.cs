using System;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Testing.Core;
using FluentDocker.Tests.Mocks;
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
    public void Constructor_NullConfigure_Throws()
    {
      Assert.Throws<ArgumentNullException>(() =>
          new VolumeResource(Kernel, null!));
    }
  }
}
