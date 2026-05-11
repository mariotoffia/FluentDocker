using System;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Testing.Core;
using FluentDocker.Tests.Mocks;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Testing
{
  [Trait("Category", "Unit")]
  public class ImageResourceTests : MockKernelTestBase, IAsyncLifetime
  {
    public async ValueTask InitializeAsync()
    {
      await InitializeMockKernelAsync();
    }

    [Fact]
    public async Task InitializeAsync_PullsImage()
    {
      MockPack
          .SetupImagePull()
          .SetupImageInspect("sha256:img001");

      var resource = new ImageResource(Kernel, "alpine", "3.18");

      await resource.InitializeAsync(TestContext.Current.CancellationToken);

      Assert.True(resource.IsInitialized);
      Assert.Equal("alpine:3.18", resource.ImageReference);
      Assert.Equal("sha256:img001", resource.ImageId);
    }

    [Fact]
    public async Task DisposeAsync_WithRemoveOnDispose_RemovesImage()
    {
      MockPack
          .SetupImagePull()
          .SetupImageInspect()
          .SetupImageRemove();

      var resource = new ImageResource(
          Kernel, "nginx", removeOnDispose: true);

      await resource.InitializeAsync(TestContext.Current.CancellationToken);
      await resource.DisposeAsync();

      Assert.False(resource.IsInitialized);
    }

    [Fact]
    public async Task DisposeAsync_WithoutRemoveOnDispose_KeepsImage()
    {
      MockPack
          .SetupImagePull()
          .SetupImageInspect("sha256:keep-me");

      var resource = new ImageResource(
          Kernel, "alpine", removeOnDispose: false);

      await resource.InitializeAsync(TestContext.Current.CancellationToken);
      var imageId = resource.ImageId;
      await resource.DisposeAsync();

      // ImageId is preserved because teardown is skipped
      Assert.Equal("sha256:keep-me", imageId);
    }

    [Fact]
    public async Task InitializeAsync_DefaultTag_UsesLatest()
    {
      MockPack
          .SetupImagePull()
          .SetupImageInspect();

      var resource = new ImageResource(Kernel, "redis");

      await resource.InitializeAsync(TestContext.Current.CancellationToken);

      Assert.Equal("redis:latest", resource.ImageReference);
    }

    [Fact]
    public void Constructor_NullImage_Throws()
    {
      Assert.Throws<ArgumentNullException>(() =>
          new ImageResource(Kernel, null!));
    }
  }
}
