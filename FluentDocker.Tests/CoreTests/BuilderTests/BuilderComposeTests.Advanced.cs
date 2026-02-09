using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Drivers;
using FluentDocker.Model.Drivers;
using FluentDocker.Tests.Mocks;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.BuilderTests
{
  /// <summary>
  /// Unit tests for ComposeBuilder - advanced options and combined scenarios.
  /// </summary>
  public partial class BuilderComposeTests
  {
    [Fact]
    public async Task WithNoStart_SetsNoStartFlag()
    {
      var (kernel, mockPack) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");
      mockPack.SetupComposeUpAsync(new ComposeUpResult { ProjectName = "test" });

      try
      {
        await using var scope = await new Builder()
            .WithinDriver("docker", kernel)
            .UseCompose(c => c
                .WithComposeFile("/compose.yml")
                .WithNoStart())
            .BuildAsync();

        mockPack.ComposeDriver.Verify(d => d.UpAsync(
            It.IsAny<DriverContext>(),
            It.Is<ComposeUpConfig>(c => c.NoStart == true),
            It.IsAny<System.Threading.CancellationToken>()), Times.Once);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task WithWait_SetsWaitFlag()
    {
      var (kernel, mockPack) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");
      mockPack.SetupComposeUpAsync(new ComposeUpResult { ProjectName = "test" });

      try
      {
        await using var scope = await new Builder()
            .WithinDriver("docker", kernel)
            .UseCompose(c => c
                .WithComposeFile("/compose.yml")
                .WithWait())
            .BuildAsync();

        mockPack.ComposeDriver.Verify(d => d.UpAsync(
            It.IsAny<DriverContext>(),
            It.Is<ComposeUpConfig>(c => c.Wait == true),
            It.IsAny<System.Threading.CancellationToken>()), Times.Once);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task WithWaitTimeout_SetsWaitTimeoutAndEnablesWait()
    {
      var (kernel, mockPack) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");
      mockPack.SetupComposeUpAsync(new ComposeUpResult { ProjectName = "test" });

      try
      {
        await using var scope = await new Builder()
            .WithinDriver("docker", kernel)
            .UseCompose(c => c
                .WithComposeFile("/compose.yml")
                .WithWaitTimeout(120))
            .BuildAsync();

        mockPack.ComposeDriver.Verify(d => d.UpAsync(
            It.IsAny<DriverContext>(),
            It.Is<ComposeUpConfig>(c => c.WaitTimeout == 120 && c.Wait == true),
            It.IsAny<System.Threading.CancellationToken>()), Times.Once);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task WithRemoveVolumes_CreatesServiceWithRemoveVolumesFlag()
    {
      var (kernel, mockPack) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");
      mockPack.SetupComposeUpAsync(new ComposeUpResult { ProjectName = "test" });
      mockPack.SetupComposeDown();

      try
      {
        await using var scope = await new Builder()
            .WithinDriver("docker", kernel)
            .UseCompose(c => c
                .WithComposeFile("/compose.yml")
                .WithRemoveVolumes())
            .BuildAsync();

        mockPack.ComposeDriver.Verify(d => d.UpAsync(
            It.IsAny<DriverContext>(),
            It.IsAny<ComposeUpConfig>(),
            It.IsAny<System.Threading.CancellationToken>()), Times.Once);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task WithRemoveImages_CreatesServiceWithRemoveImagesFlag()
    {
      var (kernel, mockPack) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");
      mockPack.SetupComposeUpAsync(new ComposeUpResult { ProjectName = "test" });
      mockPack.SetupComposeDown();

      try
      {
        await using var scope = await new Builder()
            .WithinDriver("docker", kernel)
            .UseCompose(c => c
                .WithComposeFile("/compose.yml")
                .WithRemoveImages())
            .BuildAsync();

        mockPack.ComposeDriver.Verify(d => d.UpAsync(
            It.IsAny<DriverContext>(),
            It.IsAny<ComposeUpConfig>(),
            It.IsAny<System.Threading.CancellationToken>()), Times.Once);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task WithScale_StoresScaleConfiguration()
    {
      var (kernel, mockPack) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");
      mockPack.SetupComposeUpAsync(new ComposeUpResult
      {
        ProjectName = "test",
        Services = new List<string> { "web" }
      });

      try
      {
        await using var scope = await new Builder()
            .WithinDriver("docker", kernel)
            .UseCompose(c => c
                .WithComposeFile("/compose.yml")
                .WithScale("web", 3))
            .BuildAsync();

        // Assert - Scale config should be passed through to the driver
        mockPack.ComposeDriver.Verify(d => d.UpAsync(
            It.IsAny<DriverContext>(),
            It.Is<ComposeUpConfig>(c =>
                c.Scale.ContainsKey("web") &&
                c.Scale["web"] == 3),
            It.IsAny<System.Threading.CancellationToken>()), Times.Once);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task WithProfiles_StoresProfilesConfiguration()
    {
      var (kernel, mockPack) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");
      mockPack.SetupComposeUpAsync(new ComposeUpResult { ProjectName = "test" });

      try
      {
        await using var scope = await new Builder()
            .WithinDriver("docker", kernel)
            .UseCompose(c => c
                .WithComposeFile("/compose.yml")
                .WithProfiles("debug", "development"))
            .BuildAsync();

        // Assert - Profiles should be passed through to the driver
        mockPack.ComposeDriver.Verify(d => d.UpAsync(
            It.IsAny<DriverContext>(),
            It.Is<ComposeUpConfig>(c =>
                c.Profiles.Contains("debug") &&
                c.Profiles.Contains("development")),
            It.IsAny<System.Threading.CancellationToken>()), Times.Once);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task WithPull_StoresPullConfiguration()
    {
      var (kernel, mockPack) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");
      mockPack.SetupComposeUpAsync(new ComposeUpResult { ProjectName = "test" });

      try
      {
        await using var scope = await new Builder()
            .WithinDriver("docker", kernel)
            .UseCompose(c => c
                .WithComposeFile("/compose.yml")
                .WithPull())
            .BuildAsync();

        // Assert - Pull should be set to "always" in the config
        mockPack.ComposeDriver.Verify(d => d.UpAsync(
            It.IsAny<DriverContext>(),
            It.Is<ComposeUpConfig>(c => c.Pull == "always"),
            It.IsAny<System.Threading.CancellationToken>()), Times.Once);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task CombinedOptions_AllOptionsPassedCorrectly()
    {
      var (kernel, mockPack) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");
      mockPack.SetupComposeUpAsync(new ComposeUpResult
      {
        ProjectName = "myapp",
        Services = new List<string> { "web", "db", "cache" }
      });

      try
      {
        await using var scope = await new Builder()
            .WithinDriver("docker", kernel)
            .UseCompose(c => c
                .WithComposeFiles("/compose.yml", "/compose.override.yml")
                .WithProjectName("myapp")
                .WithEnvironment("ENV", "production")
                .WithBuild()
                .WithForceRecreate()
                .WithRemoveOrphans()
                .ForServices("web", "db")
                .WithTimeout(30)
                .WithNoDeps()
                .WithWait()
                .WithWaitTimeout(60))
            .BuildAsync();

        mockPack.ComposeDriver.Verify(d => d.UpAsync(
            It.IsAny<DriverContext>(),
            It.Is<ComposeUpConfig>(c =>
                c.ComposeFiles.Count == 2 &&
                c.ProjectName == "myapp" &&
                c.Environment.ContainsKey("ENV") &&
                c.Environment["ENV"] == "production" &&
                c.Build == true &&
                c.ForceRecreate == true &&
                c.RemoveOrphans == true &&
                c.Services.Count == 2 &&
                c.Timeout == 30 &&
                c.NoDeps == true &&
                c.Wait == true &&
                c.WaitTimeout == 60),
            It.IsAny<System.Threading.CancellationToken>()), Times.Once);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task WithBuild_False_DoesNotSetBuildFlag()
    {
      var (kernel, mockPack) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");
      mockPack.SetupComposeUpAsync(new ComposeUpResult { ProjectName = "test" });

      try
      {
        await using var scope = await new Builder()
            .WithinDriver("docker", kernel)
            .UseCompose(c => c
                .WithComposeFile("/compose.yml")
                .WithBuild(false))
            .BuildAsync();

        mockPack.ComposeDriver.Verify(d => d.UpAsync(
            It.IsAny<DriverContext>(),
            It.Is<ComposeUpConfig>(c => c.Build == false),
            It.IsAny<System.Threading.CancellationToken>()), Times.Once);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task MultipleEnvironmentCalls_MergesEnvironment()
    {
      var (kernel, mockPack) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");
      mockPack.SetupComposeUpAsync(new ComposeUpResult { ProjectName = "test" });

      try
      {
        await using var scope = await new Builder()
            .WithinDriver("docker", kernel)
            .UseCompose(c => c
                .WithComposeFile("/compose.yml")
                .WithEnvironment("VAR1", "value1")
                .WithEnvironment("VAR2", "value2")
                .WithEnvironment(new Dictionary<string, string>
                {
                            { "VAR3", "value3" },
                            { "VAR4", "value4" }
                }))
            .BuildAsync();

        mockPack.ComposeDriver.Verify(d => d.UpAsync(
            It.IsAny<DriverContext>(),
            It.Is<ComposeUpConfig>(c =>
                c.Environment.Count == 4 &&
                c.Environment["VAR1"] == "value1" &&
                c.Environment["VAR2"] == "value2" &&
                c.Environment["VAR3"] == "value3" &&
                c.Environment["VAR4"] == "value4"),
            It.IsAny<System.Threading.CancellationToken>()), Times.Once);
      }
      finally { kernel.Dispose(); }
    }
  }
}
