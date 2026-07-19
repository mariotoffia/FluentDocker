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
  [Trait("Category", "Unit")]
  public sealed class ImagesDocSnippetsTests : MockKernelTestBase, IAsyncLifetime
  {
    public ValueTask InitializeAsync() => new(InitializeMockKernelAsync());

    [Fact]
    public async Task PerFileCopyPattern_BuildsDockerfileSuccessfully()
    {
      Directory.CreateDirectory(".out/images-doc-per-file");
      var package = Path.Combine(".out", "images-doc-per-file", "package.json");
      var program = Path.Combine(".out", "images-doc-per-file", "Program.cs");
      await File.WriteAllTextAsync(package, "{}", TestContext.Current.CancellationToken);
      await File.WriteAllTextAsync(program, "class Program {}", TestContext.Current.CancellationToken);
      var sawPackage = false;
      var sawProgram = false;
      SetupImageBuild(config =>
      {
        sawPackage = File.Exists(Path.Combine(config.BuildContext, package));
        sawProgram = File.Exists(Path.Combine(config.BuildContext, program));
      });

      await using var results = await new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseImage("doc-snippet:latest", df => df
              .UseParent("alpine:3.20")
              .Copy(package, "/app/package.json")
              .Copy(program, "/app/Program.cs"))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);

      Assert.Single(results.All);
      Assert.True(sawPackage);
      Assert.True(sawProgram);
    }

    [Fact]
    public async Task FromFileWithBuildContextPattern_UsesDirectoryAsBuildContext()
    {
      var context = Path.Combine(".out", "images-doc-context");
      Directory.CreateDirectory(Path.Combine(context, "src"));
      var dockerfile = Path.Combine(context, "Dockerfile");
      await File.WriteAllTextAsync(dockerfile, "FROM alpine:3.20\nCOPY src/app.txt /app/app.txt\n", TestContext.Current.CancellationToken);
      await File.WriteAllTextAsync(Path.Combine(context, "src", "app.txt"), "hello", TestContext.Current.CancellationToken);
      ImageBuildConfig? captured = null;
      SetupImageBuild(config => captured = config);

      await using var results = await new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseImage("doc-context:latest", df => df
              .FromFile(dockerfile)
              .WithBuildContext(context))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);

      Assert.Single(results.All);
      Assert.Equal(Path.GetFullPath(context), captured?.BuildContext);
      Assert.Equal("Dockerfile", captured?.DockerfileName);
    }

    [Fact]
    public async Task DirectoryCopySource_ThrowsDocumentedRestriction()
    {
      var source = Path.Combine(".out", "images-doc-dir-source");
      Directory.CreateDirectory(source);

      var ex = await Assert.ThrowsAsync<NotSupportedException>(() => new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseImage("bad-doc:latest", df => df
              .UseParent("alpine:3.20")
              .Copy(source, "/app/src/"))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken));

      Assert.Equal("Directory sources are not supported by DockerfileBuilder; add files individually.", ex.Message);
    }

    private void SetupImageBuild(Action<ImageBuildConfig> capture)
    {
      MockPack.ImageDriver
          .Setup(d => d.BuildAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<ImageBuildConfig>(),
              It.IsAny<IProgress<ImageBuildProgress>>(),
              It.IsAny<CancellationToken>()))
          .Callback<DriverContext, ImageBuildConfig, IProgress<ImageBuildProgress>, CancellationToken>((_, cfg, _, _) => capture(cfg))
          .ReturnsAsync(CommandResponse<ImageBuildResult>.Ok(new ImageBuildResult { ImageId = "sha256:doc" }));
    }
  }
}
