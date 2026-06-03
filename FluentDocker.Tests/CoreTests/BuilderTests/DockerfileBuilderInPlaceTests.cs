using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Model.Drivers;
using FluentDocker.Tests.Mocks;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.BuilderTests
{
  /// <summary>
  /// Tests for in-place builds via <c>FromFile(...).WithBuildContext(...)</c> (issue #280):
  /// an existing Dockerfile must be used in place (passed via <c>--file</c>) without rendering
  /// or copying a generated Dockerfile into the user's working directory.
  /// </summary>
  [Trait("Category", "Unit")]
  public sealed class DockerfileBuilderInPlaceTests : MockKernelTestBase, IAsyncLifetime
  {
    public ValueTask InitializeAsync() => new(InitializeMockKernelAsync());

    private static string NewTempDir()
    {
      var dir = Path.Combine(Path.GetTempPath(), "fd280-" + Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(dir);
      return dir;
    }

    private static void SafeDelete(string dir)
    {
      try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
      catch (IOException) { /* best effort */ }
    }

    [Fact]
    public async Task WithBuildContext_DoesNotLeaveGeneratedDockerfileInContext()
    {
      var root = NewTempDir();
      try
      {
        // Dockerfile lives in a sub-directory; build context is the parent (the #280 scenario).
        var sub = Path.Combine(root, "app");
        Directory.CreateDirectory(sub);
        var dockerfile = Path.Combine(sub, "Dockerfile");
        await File.WriteAllTextAsync(dockerfile, "FROM alpine:3.20\nCMD [\"true\"]\n",
            TestContext.Current.CancellationToken);

        var contents = await new DockerfileBuilder()
            .FromFile(dockerfile)
            .WithBuildContext(root)
            .ToDockerfileStringAsync();

        // The existing file's contents are returned…
        Assert.Contains("FROM alpine:3.20", contents);
        // …and NO generated Dockerfile is written into the build context root.
        Assert.False(File.Exists(Path.Combine(root, "Dockerfile")),
            "in-place build must not render a Dockerfile into the build context");
      }
      finally { SafeDelete(root); }
    }

    [Fact]
    public async Task WithBuildContext_PassesRelativeDockerfileNameAndContextToDriver()
    {
      var root = NewTempDir();
      try
      {
        var sub = Path.Combine(root, "app");
        Directory.CreateDirectory(sub);
        var dockerfile = Path.Combine(sub, "Dockerfile");
        await File.WriteAllTextAsync(dockerfile, "FROM alpine:3.20\n",
            TestContext.Current.CancellationToken);

        ImageBuildConfig captured = null;
        MockPack.ImageDriver
            .Setup(d => d.BuildAsync(
                It.IsAny<DriverContext>(),
                It.IsAny<ImageBuildConfig>(),
                It.IsAny<IProgress<ImageBuildProgress>>(),
                It.IsAny<CancellationToken>()))
            .Callback<DriverContext, ImageBuildConfig, IProgress<ImageBuildProgress>, CancellationToken>(
                (_, cfg, _, _) => captured = cfg)
            .ReturnsAsync(CommandResponse<ImageBuildResult>.Ok(
                new ImageBuildResult { ImageId = "sha256:deadbeef" }));

        await new Builder()
            .WithinDriver(DriverId, Kernel)
            .UseImage("inplace-img:latest", df => df
                .FromFile(dockerfile)
                .WithBuildContext(root))
            .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(captured);
        Assert.Equal(Path.GetFullPath(root), captured.BuildContext);
        Assert.Equal("app/Dockerfile", captured.DockerfileName);
      }
      finally { SafeDelete(root); }
    }

    [Fact]
    public async Task WithBuildContext_DockerfileOutsideContext_Throws()
    {
      var root = NewTempDir();
      try
      {
        var dockerfile = Path.Combine(root, "Dockerfile");
        await File.WriteAllTextAsync(dockerfile, "FROM alpine\n", TestContext.Current.CancellationToken);
        var otherContext = Path.Combine(root, "elsewhere");
        Directory.CreateDirectory(otherContext);

        var builder = new DockerfileBuilder()
            .FromFile(dockerfile)
            .WithBuildContext(otherContext);

        await Assert.ThrowsAsync<FluentDockerException>(() => builder.ToDockerfileStringAsync());
      }
      finally { SafeDelete(root); }
    }

    [Fact]
    public async Task WithoutBuildContext_NormalRenderPath_StillProducesDockerfile()
    {
      // Regression guard: the default (non-in-place) path is unaffected.
      var contents = await new DockerfileBuilder()
          .UseParent("alpine:3.20")
          .Run("echo hi")
          .ToDockerfileStringAsync();

      Assert.Contains("FROM alpine:3.20", contents);
      Assert.Contains("RUN echo hi", contents);
    }
  }
}
