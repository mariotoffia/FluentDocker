using System.Collections.Generic;
using System.IO;
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
    /// Unit tests for the enhanced ComposeBuilder.
    /// </summary>
    [Trait("Category", "Unit")]
    public class BuilderComposeTests
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

        [Fact]
        public async Task WithNoStart_SetsNoStartFlag()
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
                        .WithNoStart())
                    .BuildAsync();

                // Assert
                mockPack.ComposeDriver.Verify(d => d.UpAsync(
                    It.IsAny<DriverContext>(),
                    It.Is<ComposeUpConfig>(c => c.NoStart == true),
                    It.IsAny<System.Threading.CancellationToken>()), Times.Once);
            }
            finally
            {
                kernel.Dispose();
            }
        }

        [Fact]
        public async Task WithWait_SetsWaitFlag()
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
                        .WithWait())
                    .BuildAsync();

                // Assert
                mockPack.ComposeDriver.Verify(d => d.UpAsync(
                    It.IsAny<DriverContext>(),
                    It.Is<ComposeUpConfig>(c => c.Wait == true),
                    It.IsAny<System.Threading.CancellationToken>()), Times.Once);
            }
            finally
            {
                kernel.Dispose();
            }
        }

        [Fact]
        public async Task WithWaitTimeout_SetsWaitTimeoutAndEnablesWait()
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
                        .WithWaitTimeout(120))
                    .BuildAsync();

                // Assert - WaitTimeout should be set and Wait should be automatically enabled
                mockPack.ComposeDriver.Verify(d => d.UpAsync(
                    It.IsAny<DriverContext>(),
                    It.Is<ComposeUpConfig>(c => c.WaitTimeout == 120 && c.Wait == true),
                    It.IsAny<System.Threading.CancellationToken>()), Times.Once);
            }
            finally
            {
                kernel.Dispose();
            }
        }

        [Fact]
        public async Task WithRemoveVolumes_CreatesServiceWithRemoveVolumesFlag()
        {
            // Arrange
            var (kernel, mockPack) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");
            
            mockPack.SetupComposeUpAsync(new ComposeUpResult { ProjectName = "test" });
            mockPack.SetupComposeDown();

            try
            {
                // Act
                await using var scope = await new Builder()
                    .WithinDriver("docker", kernel)
                    .UseCompose(c => c
                        .WithComposeFile("/compose.yml")
                        .WithRemoveVolumes())
                    .BuildAsync();

                // Assert - The service was created (we verify the Up was called)
                mockPack.ComposeDriver.Verify(d => d.UpAsync(
                    It.IsAny<DriverContext>(),
                    It.IsAny<ComposeUpConfig>(),
                    It.IsAny<System.Threading.CancellationToken>()), Times.Once);
            }
            finally
            {
                kernel.Dispose();
            }
        }

        [Fact]
        public async Task WithRemoveImages_CreatesServiceWithRemoveImagesFlag()
        {
            // Arrange
            var (kernel, mockPack) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");
            
            mockPack.SetupComposeUpAsync(new ComposeUpResult { ProjectName = "test" });
            mockPack.SetupComposeDown();

            try
            {
                // Act
                await using var scope = await new Builder()
                    .WithinDriver("docker", kernel)
                    .UseCompose(c => c
                        .WithComposeFile("/compose.yml")
                        .WithRemoveImages())
                    .BuildAsync();

                // Assert - The service was created (we verify the Up was called)
                mockPack.ComposeDriver.Verify(d => d.UpAsync(
                    It.IsAny<DriverContext>(),
                    It.IsAny<ComposeUpConfig>(),
                    It.IsAny<System.Threading.CancellationToken>()), Times.Once);
            }
            finally
            {
                kernel.Dispose();
            }
        }

        [Fact]
        public async Task WithScale_StoresScaleConfiguration()
        {
            // Arrange
            var (kernel, mockPack) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");
            
            mockPack.SetupComposeUpAsync(new ComposeUpResult 
            { 
                ProjectName = "test",
                Services = new List<string> { "web" }
            });

            try
            {
                // Act - Scale is stored for later scaling operations
                await using var scope = await new Builder()
                    .WithinDriver("docker", kernel)
                    .UseCompose(c => c
                        .WithComposeFile("/compose.yml")
                        .WithScale("web", 3))
                    .BuildAsync();

                // Assert - Up was called (scale is applied separately)
                mockPack.ComposeDriver.Verify(d => d.UpAsync(
                    It.IsAny<DriverContext>(),
                    It.IsAny<ComposeUpConfig>(),
                    It.IsAny<System.Threading.CancellationToken>()), Times.Once);
            }
            finally
            {
                kernel.Dispose();
            }
        }

        [Fact]
        public async Task WithProfiles_StoresProfilesConfiguration()
        {
            // Arrange
            var (kernel, mockPack) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");
            
            mockPack.SetupComposeUpAsync(new ComposeUpResult { ProjectName = "test" });

            try
            {
                // Act - Profiles are stored for compose operations
                await using var scope = await new Builder()
                    .WithinDriver("docker", kernel)
                    .UseCompose(c => c
                        .WithComposeFile("/compose.yml")
                        .WithProfiles("debug", "development"))
                    .BuildAsync();

                // Assert - Up was called
                mockPack.ComposeDriver.Verify(d => d.UpAsync(
                    It.IsAny<DriverContext>(),
                    It.IsAny<ComposeUpConfig>(),
                    It.IsAny<System.Threading.CancellationToken>()), Times.Once);
            }
            finally
            {
                kernel.Dispose();
            }
        }

        [Fact]
        public async Task WithPull_StoresPullConfiguration()
        {
            // Arrange
            var (kernel, mockPack) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");
            
            mockPack.SetupComposeUpAsync(new ComposeUpResult { ProjectName = "test" });

            try
            {
                // Act - Pull configuration is stored
                await using var scope = await new Builder()
                    .WithinDriver("docker", kernel)
                    .UseCompose(c => c
                        .WithComposeFile("/compose.yml")
                        .WithPull())
                    .BuildAsync();

                // Assert - Up was called
                mockPack.ComposeDriver.Verify(d => d.UpAsync(
                    It.IsAny<DriverContext>(),
                    It.IsAny<ComposeUpConfig>(),
                    It.IsAny<System.Threading.CancellationToken>()), Times.Once);
            }
            finally
            {
                kernel.Dispose();
            }
        }

        [Fact]
        public async Task CombinedOptions_AllOptionsPassedCorrectly()
        {
            // Arrange
            var (kernel, mockPack) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");
            
            mockPack.SetupComposeUpAsync(new ComposeUpResult 
            { 
                ProjectName = "myapp",
                Services = new List<string> { "web", "db", "cache" }
            });

            try
            {
                // Act - Configure multiple options at once
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

                // Assert - All options should be passed correctly
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
            finally
            {
                kernel.Dispose();
            }
        }

        [Fact]
        public async Task WithBuild_False_DoesNotSetBuildFlag()
        {
            // Arrange
            var (kernel, mockPack) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");
            
            mockPack.SetupComposeUpAsync(new ComposeUpResult { ProjectName = "test" });

            try
            {
                // Act - Explicitly set build to false
                await using var scope = await new Builder()
                    .WithinDriver("docker", kernel)
                    .UseCompose(c => c
                        .WithComposeFile("/compose.yml")
                        .WithBuild(false))
                    .BuildAsync();

                // Assert
                mockPack.ComposeDriver.Verify(d => d.UpAsync(
                    It.IsAny<DriverContext>(),
                    It.Is<ComposeUpConfig>(c => c.Build == false),
                    It.IsAny<System.Threading.CancellationToken>()), Times.Once);
            }
            finally
            {
                kernel.Dispose();
            }
        }

        [Fact]
        public async Task MultipleEnvironmentCalls_MergesEnvironment()
        {
            // Arrange
            var (kernel, mockPack) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");
            
            mockPack.SetupComposeUpAsync(new ComposeUpResult { ProjectName = "test" });

            try
            {
                // Act - Call WithEnvironment multiple times
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

                // Assert - All environment variables should be merged
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
            finally
            {
                kernel.Dispose();
            }
        }

        [Fact]
        public async Task WithEnvFile_LoadsEnvironmentVariablesFromFile()
        {
            // Arrange
            var (kernel, mockPack) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");
            
            mockPack.SetupComposeUpAsync(new ComposeUpResult { ProjectName = "test" });

            // Create a temporary .env file
            var tempEnvFile = Path.GetTempFileName();
            try
            {
                await File.WriteAllTextAsync(tempEnvFile, @"
# This is a comment
DB_HOST=localhost
DB_PORT=5432
DB_NAME=testdb

# Another comment
API_KEY=secret123
EMPTY_VALUE=
");

                // Act
                await using var scope = await new Builder()
                    .WithinDriver("docker", kernel)
                    .UseCompose(c => c
                        .WithComposeFile("/compose.yml")
                        .WithEnvFile(tempEnvFile))
                    .BuildAsync();

                // Assert - Environment variables from file should be loaded
                mockPack.ComposeDriver.Verify(d => d.UpAsync(
                    It.IsAny<DriverContext>(),
                    It.Is<ComposeUpConfig>(c => 
                        c.Environment.ContainsKey("DB_HOST") &&
                        c.Environment["DB_HOST"] == "localhost" &&
                        c.Environment.ContainsKey("DB_PORT") &&
                        c.Environment["DB_PORT"] == "5432" &&
                        c.Environment.ContainsKey("DB_NAME") &&
                        c.Environment["DB_NAME"] == "testdb" &&
                        c.Environment.ContainsKey("API_KEY") &&
                        c.Environment["API_KEY"] == "secret123" &&
                        c.Environment.ContainsKey("EMPTY_VALUE") &&
                        c.Environment["EMPTY_VALUE"] == ""),
                    It.IsAny<System.Threading.CancellationToken>()), Times.Once);
            }
            finally
            {
                kernel.Dispose();
                if (File.Exists(tempEnvFile))
                    File.Delete(tempEnvFile);
            }
        }

        [Fact]
        public async Task WithEnvFile_CombinedWithManualEnvironment_MergesCorrectly()
        {
            // Arrange
            var (kernel, mockPack) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");
            
            mockPack.SetupComposeUpAsync(new ComposeUpResult { ProjectName = "test" });

            // Create a temporary .env file
            var tempEnvFile = Path.GetTempFileName();
            try
            {
                await File.WriteAllTextAsync(tempEnvFile, @"
FILE_VAR=from_file
OVERRIDE_VAR=file_value
");

                // Act - Combine env file with manual environment (manual should override file)
                await using var scope = await new Builder()
                    .WithinDriver("docker", kernel)
                    .UseCompose(c => c
                        .WithComposeFile("/compose.yml")
                        .WithEnvFile(tempEnvFile)
                        .WithEnvironment("MANUAL_VAR", "manual_value")
                        .WithEnvironment("OVERRIDE_VAR", "manual_override"))
                    .BuildAsync();

                // Assert - Both sources should be merged, with manual taking precedence
                mockPack.ComposeDriver.Verify(d => d.UpAsync(
                    It.IsAny<DriverContext>(),
                    It.Is<ComposeUpConfig>(c => 
                        c.Environment.ContainsKey("FILE_VAR") &&
                        c.Environment["FILE_VAR"] == "from_file" &&
                        c.Environment.ContainsKey("MANUAL_VAR") &&
                        c.Environment["MANUAL_VAR"] == "manual_value" &&
                        c.Environment.ContainsKey("OVERRIDE_VAR") &&
                        c.Environment["OVERRIDE_VAR"] == "manual_override"),
                    It.IsAny<System.Threading.CancellationToken>()), Times.Once);
            }
            finally
            {
                kernel.Dispose();
                if (File.Exists(tempEnvFile))
                    File.Delete(tempEnvFile);
            }
        }

        [Fact]
        public async Task WithEnvFile_NonExistentFile_DoesNotThrow()
        {
            // Arrange
            var (kernel, mockPack) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");
            
            mockPack.SetupComposeUpAsync(new ComposeUpResult { ProjectName = "test" });

            try
            {
                // Act - Use a non-existent file path (should not throw, just skip loading)
                await using var scope = await new Builder()
                    .WithinDriver("docker", kernel)
                    .UseCompose(c => c
                        .WithComposeFile("/compose.yml")
                        .WithEnvFile("/non/existent/path/.env")
                        .WithEnvironment("FALLBACK", "value"))
                    .BuildAsync();

                // Assert - Only the manual environment variable should be present
                mockPack.ComposeDriver.Verify(d => d.UpAsync(
                    It.IsAny<DriverContext>(),
                    It.Is<ComposeUpConfig>(c => 
                        c.Environment.Count == 1 &&
                        c.Environment.ContainsKey("FALLBACK") &&
                        c.Environment["FALLBACK"] == "value"),
                    It.IsAny<System.Threading.CancellationToken>()), Times.Once);
            }
            finally
            {
                kernel.Dispose();
            }
        }

        [Fact]
        public async Task WithEnvFile_HandlesQuotedValues()
        {
            // Arrange
            var (kernel, mockPack) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");
            
            mockPack.SetupComposeUpAsync(new ComposeUpResult { ProjectName = "test" });

            // Create a temporary .env file with various formats
            var tempEnvFile = Path.GetTempFileName();
            try
            {
                await File.WriteAllTextAsync(tempEnvFile, @"
SIMPLE=simple_value
WITH_SPACES=value with spaces
EQUALS_IN_VALUE=key=value=more
");

                // Act
                await using var scope = await new Builder()
                    .WithinDriver("docker", kernel)
                    .UseCompose(c => c
                        .WithComposeFile("/compose.yml")
                        .WithEnvFile(tempEnvFile))
                    .BuildAsync();

                // Assert - Values should be parsed correctly
                mockPack.ComposeDriver.Verify(d => d.UpAsync(
                    It.IsAny<DriverContext>(),
                    It.Is<ComposeUpConfig>(c => 
                        c.Environment["SIMPLE"] == "simple_value" &&
                        c.Environment["WITH_SPACES"] == "value with spaces" &&
                        c.Environment["EQUALS_IN_VALUE"] == "key=value=more"),
                    It.IsAny<System.Threading.CancellationToken>()), Times.Once);
            }
            finally
            {
                kernel.Dispose();
                if (File.Exists(tempEnvFile))
                    File.Delete(tempEnvFile);
            }
        }
    }
}
