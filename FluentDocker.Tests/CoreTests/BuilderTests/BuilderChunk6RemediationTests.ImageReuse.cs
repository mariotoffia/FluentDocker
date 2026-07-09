using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Drivers;
using FluentDocker.Model.Drivers;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.BuilderTests
{
  public partial class BuilderChunk6RemediationTests
  {
    [Fact]
    public async Task ImageBuilder_ReuseWithMissingTag_BuildsAllTags()
    {
      MockPack.ImageDriver
          .Setup(d => d.ListAsync(
              It.IsAny<DriverContext>(), It.IsAny<ImageListFilter>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync((DriverContext _, ImageListFilter filter, CancellationToken _) =>
              CommandResponse<IList<Image>>.Ok(filter.Reference == "repo:v2"
                  ? [new Image { Id = "sha256:existing" }]
                  : []));
      MockPack.ImageDriver
          .Setup(d => d.BuildAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<ImageBuildConfig>(),
              It.IsAny<IProgress<ImageBuildProgress>>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<ImageBuildResult>.Ok(new ImageBuildResult
          {
            ImageId = "sha256:built"
          }));
      var dockerfile = new ImageBuilder(Kernel, DriverId, "repo:v2")
          .FromString("FROM scratch");
      dockerfile.WorkingFolder(".out/image-reuse-missing-tag");
      dockerfile.ToImage().ImageTag("latest").ReuseIfAlreadyExists();

      await using var image = await dockerfile.BuildAsync(TestContext.Current.CancellationToken);

      MockPack.ImageDriver.Verify(d => d.BuildAsync(
          It.IsAny<DriverContext>(),
          It.Is<ImageBuildConfig>(cfg =>
              cfg.Tags.SequenceEqual(new[] { "repo:v2", "repo:latest" })),
          It.IsAny<IProgress<ImageBuildProgress>>(),
          It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ImageBuilder_ReuseWithAllTagsSameImage_SkipsBuild()
    {
      var references = new List<string>();
      MockPack.ImageDriver
          .Setup(d => d.ListAsync(
              It.IsAny<DriverContext>(), It.IsAny<ImageListFilter>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync((DriverContext _, ImageListFilter filter, CancellationToken _) =>
          {
            references.Add(filter.Reference);
            return CommandResponse<IList<Image>>.Ok([new Image { Id = "sha256:existing" }]);
          });
      var dockerfile = new ImageBuilder(Kernel, DriverId, "repo:v2")
          .FromString("FROM scratch");
      dockerfile.WorkingFolder(".out/image-reuse-all-tags");
      dockerfile.ToImage().ImageTag("latest").ReuseIfAlreadyExists();

      await using var image = await dockerfile.BuildAsync(TestContext.Current.CancellationToken);

      Assert.Equal(new[] { "repo:v2", "repo:latest" }, references);
      MockPack.ImageDriver.Verify(d => d.BuildAsync(
          It.IsAny<DriverContext>(), It.IsAny<ImageBuildConfig>(),
          It.IsAny<IProgress<ImageBuildProgress>>(), It.IsAny<CancellationToken>()), Times.Never);
    }
  }
}
