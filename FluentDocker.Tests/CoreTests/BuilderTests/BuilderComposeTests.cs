using System.Collections.Generic;
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
  /// Unit tests for the enhanced ComposeBuilder - basic options and environment.
  /// </summary>
  [Trait("Category", "Unit")]
  public partial class BuilderComposeTests
  {
    [Fact]
    public async Task WithComposeFile_AddsFile()
    {
      // Arrange
      var (kernel, mockPack) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");

      mockPack.SetupComposeUpAsync(new ComposeUpResult
      {
        ProjectName = "testproject",
        Services = new List<string> { "web" }
      });

      try
      {
        // Act - build the compose configuration
        await using var scope = await new Builder()
            .WithinDriver("docker", kernel)
            .UseCompose(c => c
                .WithComposeFile("/path/to/docker-compose.yml")
                .WithProjectName("testproject"))
            .BuildAsync();

        // Assert
        mockPack.ComposeDriver.Verify(d => d.UpAsync(
            It.IsAny<DriverContext>(),
            It.Is<ComposeUpConfig>(c => c.ComposeFiles.Contains("/path/to/docker-compose.yml")),
            It.IsAny<System.Threading.CancellationToken>()), Times.Once);
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    public async Task WithComposeFiles_AddsMultipleFiles()
    {
      // Arrange
      var (kernel, mockPack) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");

      mockPack.SetupComposeUpAsync(new ComposeUpResult
      {
        ProjectName = "testproject",
        Services = new List<string> { "web", "db" }
      });

      try
      {
        // Act
        await using var scope = await new Builder()
            .WithinDriver("docker", kernel)
            .UseCompose(c => c
                .WithComposeFiles(
                    "/path/to/docker-compose.yml",
                    "/path/to/docker-compose.override.yml")
                .WithProjectName("testproject"))
            .BuildAsync();

        // Assert
        mockPack.ComposeDriver.Verify(d => d.UpAsync(
            It.IsAny<DriverContext>(),
            It.Is<ComposeUpConfig>(c =>
                c.ComposeFiles.Count == 2 &&
                c.ComposeFiles.Contains("/path/to/docker-compose.yml") &&
                c.ComposeFiles.Contains("/path/to/docker-compose.override.yml")),
            It.IsAny<System.Threading.CancellationToken>()), Times.Once);
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    public async Task WithProjectName_SetsProjectName()
    {
      // Arrange
      var (kernel, mockPack) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");

      mockPack.SetupComposeUpAsync(new ComposeUpResult
      {
        ProjectName = "myapp",
        Services = new List<string>()
      });

      try
      {
        // Act
        await using var scope = await new Builder()
            .WithinDriver("docker", kernel)
            .UseCompose(c => c
                .WithComposeFile("/compose.yml")
                .WithProjectName("myapp"))
            .BuildAsync();

        // Assert
        mockPack.ComposeDriver.Verify(d => d.UpAsync(
            It.IsAny<DriverContext>(),
            It.Is<ComposeUpConfig>(c => c.ProjectName == "myapp"),
            It.IsAny<System.Threading.CancellationToken>()), Times.Once);
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    public async Task WithEnvironment_SingleKeyValue_SetsVariable()
    {
      // Arrange
      var (kernel, mockPack) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");

      mockPack.SetupComposeUpAsync(new ComposeUpResult { ProjectName = "test" });

      try
      {
        // Act
        await using var scope = await new Builder()
            .WithinDriver("docker", kernel)
            .UseCompose(c => c
                .WithComposeFile("/compose.yml")
                .WithEnvironment("DB_HOST", "localhost"))
            .BuildAsync();

        // Assert
        mockPack.ComposeDriver.Verify(d => d.UpAsync(
            It.IsAny<DriverContext>(),
            It.Is<ComposeUpConfig>(c =>
                c.Environment.ContainsKey("DB_HOST") &&
                c.Environment["DB_HOST"] == "localhost"),
            It.IsAny<System.Threading.CancellationToken>()), Times.Once);
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    public async Task WithEnvironment_Dictionary_SetsMultipleVariables()
    {
      // Arrange
      var (kernel, mockPack) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");

      mockPack.SetupComposeUpAsync(new ComposeUpResult { ProjectName = "test" });

      var envVars = new Dictionary<string, string>
            {
                { "DB_HOST", "localhost" },
                { "DB_PORT", "5432" },
                { "DB_NAME", "testdb" }
            };

      try
      {
        // Act
        await using var scope = await new Builder()
            .WithinDriver("docker", kernel)
            .UseCompose(c => c
                .WithComposeFile("/compose.yml")
                .WithEnvironment(envVars))
            .BuildAsync();

        // Assert
        mockPack.ComposeDriver.Verify(d => d.UpAsync(
            It.IsAny<DriverContext>(),
            It.Is<ComposeUpConfig>(c =>
                c.Environment.Count == 3 &&
                c.Environment["DB_HOST"] == "localhost" &&
                c.Environment["DB_PORT"] == "5432" &&
                c.Environment["DB_NAME"] == "testdb"),
            It.IsAny<System.Threading.CancellationToken>()), Times.Once);
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    public async Task WithBuild_SetsBuildFlag()
    {
      // Arrange
      var (kernel, mockPack) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");

      mockPack.SetupComposeUpAsync(new ComposeUpResult { ProjectName = "test" });

      try
      {
        // Act
        await using var scope = await new Builder()
            .WithinDriver("docker", kernel)
            .UseCompose(c => c
                .WithComposeFile("/compose.yml")
                .WithBuild())
            .BuildAsync();

        // Assert
        mockPack.ComposeDriver.Verify(d => d.UpAsync(
            It.IsAny<DriverContext>(),
            It.Is<ComposeUpConfig>(c => c.Build == true),
            It.IsAny<System.Threading.CancellationToken>()), Times.Once);
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    public async Task WithForceRecreate_SetsForceRecreateFlag()
    {
      // Arrange
      var (kernel, mockPack) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");

      mockPack.SetupComposeUpAsync(new ComposeUpResult { ProjectName = "test" });

      try
      {
        // Act
        await using var scope = await new Builder()
            .WithinDriver("docker", kernel)
            .UseCompose(c => c
                .WithComposeFile("/compose.yml")
                .WithForceRecreate())
            .BuildAsync();

        // Assert
        mockPack.ComposeDriver.Verify(d => d.UpAsync(
            It.IsAny<DriverContext>(),
            It.Is<ComposeUpConfig>(c => c.ForceRecreate == true),
            It.IsAny<System.Threading.CancellationToken>()), Times.Once);
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    public async Task ForServices_FiltersToSpecificServices()
    {
      // Arrange
      var (kernel, mockPack) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");

      mockPack.SetupComposeUpAsync(new ComposeUpResult
      {
        ProjectName = "test",
        Services = new List<string> { "web", "api" }
      });

      try
      {
        // Act
        await using var scope = await new Builder()
            .WithinDriver("docker", kernel)
            .UseCompose(c => c
                .WithComposeFile("/compose.yml")
                .ForServices("web", "api"))
            .BuildAsync();

        // Assert
        mockPack.ComposeDriver.Verify(d => d.UpAsync(
            It.IsAny<DriverContext>(),
            It.Is<ComposeUpConfig>(c =>
                c.Services.Count == 2 &&
                c.Services.Contains("web") &&
                c.Services.Contains("api")),
            It.IsAny<System.Threading.CancellationToken>()), Times.Once);
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    public async Task WithTimeout_SetsTimeout()
    {
      // Arrange
      var (kernel, mockPack) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");

      mockPack.SetupComposeUpAsync(new ComposeUpResult { ProjectName = "test" });

      try
      {
        // Act
        await using var scope = await new Builder()
            .WithinDriver("docker", kernel)
            .UseCompose(c => c
                .WithComposeFile("/compose.yml")
                .WithTimeout(60))
            .BuildAsync();

        // Assert
        mockPack.ComposeDriver.Verify(d => d.UpAsync(
            It.IsAny<DriverContext>(),
            It.Is<ComposeUpConfig>(c => c.Timeout == 60),
            It.IsAny<System.Threading.CancellationToken>()), Times.Once);
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    public async Task WithNoDeps_SetsNoDepsFlag()
    {
      // Arrange
      var (kernel, mockPack) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");

      mockPack.SetupComposeUpAsync(new ComposeUpResult { ProjectName = "test" });

      try
      {
        // Act
        await using var scope = await new Builder()
            .WithinDriver("docker", kernel)
            .UseCompose(c => c
                .WithComposeFile("/compose.yml")
                .WithNoDeps())
            .BuildAsync();

        // Assert
        mockPack.ComposeDriver.Verify(d => d.UpAsync(
            It.IsAny<DriverContext>(),
            It.Is<ComposeUpConfig>(c => c.NoDeps == true),
            It.IsAny<System.Threading.CancellationToken>()), Times.Once);
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    public async Task WithRemoveOrphans_SetsRemoveOrphansFlag()
    {
      // Arrange
      var (kernel, mockPack) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");

      mockPack.SetupComposeUpAsync(new ComposeUpResult { ProjectName = "test" });

      try
      {
        // Act
        await using var scope = await new Builder()
            .WithinDriver("docker", kernel)
            .UseCompose(c => c
                .WithComposeFile("/compose.yml")
                .WithRemoveOrphans())
            .BuildAsync();

        // Assert
        mockPack.ComposeDriver.Verify(d => d.UpAsync(
            It.IsAny<DriverContext>(),
            It.Is<ComposeUpConfig>(c => c.RemoveOrphans == true),
            It.IsAny<System.Threading.CancellationToken>()), Times.Once);
      }
      finally
      {
        kernel.Dispose();
      }
    }
  }
}
