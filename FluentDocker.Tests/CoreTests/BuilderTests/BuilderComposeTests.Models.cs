using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Builders.Compose;
using FluentDocker.Drivers;
using FluentDocker.Model.Drivers;
using FluentDocker.Tests.Mocks;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.BuilderTests
{
  /// <summary>
  /// Unit tests for the first-class <c>WithModels(...)</c> integration (L3): the
  /// builder renders a Compose <c>models:</c> overlay to a managed temp file, appends
  /// it to the compose-files list, and deletes it on service teardown / dispose.
  /// </summary>
  public partial class BuilderComposeTests
  {
    [Fact]
    public async Task WithModels_AppendsRenderedOverlayFile()
    {
      // Arrange
      var (kernel, mockPack) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");
      mockPack.SetupComposeUpAsync(new ComposeUpResult { ProjectName = "modelapp" });
      mockPack.SetupComposeDown();

      string? overlayPath = null;
      string? overlayContent = null;

      try
      {
        // Act
        await using var scope = await new Builder()
            .WithinDriver("docker", kernel)
            .UseCompose(c => c
                .WithComposeFile("/path/to/docker-compose.yml")
                .WithProjectName("modelapp")
                .WithModels(m =>
                {
                  m.AddModel("llm", s => s.WithModel("ai/smollm2").WithContextSize(4096));
                  m.BindToService("app", "llm");
                  m.BindToService("api", "llm", endpointVar: "AI_URL", modelVar: "AI_MODEL");
                }))
            .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert: a SECOND compose file (the overlay) was appended after the user's file.
        var compose = scope.ComposeServices.Single();
        Assert.Equal(2, compose.ComposeFiles.Count);
        Assert.Equal("/path/to/docker-compose.yml", compose.ComposeFiles[0]);

        overlayPath = compose.ComposeFiles[1];
        Assert.True(File.Exists(overlayPath), "overlay temp file should exist while the service is alive");
        overlayContent = File.ReadAllText(overlayPath);

        // The rendered overlay contains the top-level models: map and the bindings.
        Assert.Contains("models:", overlayContent);
        Assert.Contains("  llm:", overlayContent);
        Assert.Contains("    model: ai/smollm2", overlayContent);
        Assert.Contains("    context_size: 4096", overlayContent);
        Assert.Contains("  app:", overlayContent);
        Assert.Contains("      - llm", overlayContent);
        Assert.Contains("        endpoint_var: AI_URL", overlayContent);
        Assert.Contains("        model_var: AI_MODEL", overlayContent);

        // The driver received the overlay as an extra -f.
        mockPack.ComposeDriver.Verify(d => d.UpAsync(
            It.IsAny<DriverContext>(),
            It.Is<ComposeUpConfig>(cfg =>
                cfg.ComposeFiles.Count == 2 &&
                cfg.ComposeFiles.Contains("/path/to/docker-compose.yml") &&
                cfg.ComposeFiles.Contains(overlayPath)),
            It.IsAny<System.Threading.CancellationToken>()), Times.Once);
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    public async Task WithModels_DeletesOverlayFileOnDispose()
    {
      // Arrange
      var (kernel, mockPack) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");
      mockPack.SetupComposeUpAsync(new ComposeUpResult { ProjectName = "modelapp" });
      mockPack.SetupComposeDown();

      string overlayPath;

      try
      {
        // Act
        var scope = await new Builder()
            .WithinDriver("docker", kernel)
            .UseCompose(c => c
                .WithComposeFile("/path/to/docker-compose.yml")
                .WithProjectName("modelapp")
                .WithModels(m =>
                {
                  m.AddModel("llm", s => s.WithModel("ai/smollm2"));
                  m.BindToService("app", "llm");
                }))
            .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);

        var compose = scope.ComposeServices.Single();
        overlayPath = compose.ComposeFiles[^1];
        Assert.True(File.Exists(overlayPath), "overlay temp file should exist before dispose");

        // Tear down the built service.
        await scope.DisposeAsync();

        // Assert: the managed overlay temp file is gone after dispose.
        Assert.False(File.Exists(overlayPath), "overlay temp file must be deleted on teardown/dispose");
      }
      finally
      {
        kernel.Dispose();
      }
    }
  }
}
