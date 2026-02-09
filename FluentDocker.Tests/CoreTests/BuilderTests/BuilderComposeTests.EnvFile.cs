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
    /// Unit tests for ComposeBuilder - env file handling.
    /// </summary>
    public partial class BuilderComposeTests
    {
        [Fact]
        public async Task WithEnvFile_LoadsEnvironmentVariablesFromFile()
        {
            var (kernel, mockPack) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");
            mockPack.SetupComposeUpAsync(new ComposeUpResult { ProjectName = "test" });

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

                await using var scope = await new Builder()
                    .WithinDriver("docker", kernel)
                    .UseCompose(c => c
                        .WithComposeFile("/compose.yml")
                        .WithEnvFile(tempEnvFile))
                    .BuildAsync();

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
            var (kernel, mockPack) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");
            mockPack.SetupComposeUpAsync(new ComposeUpResult { ProjectName = "test" });

            var tempEnvFile = Path.GetTempFileName();
            try
            {
                await File.WriteAllTextAsync(tempEnvFile, @"
FILE_VAR=from_file
OVERRIDE_VAR=file_value
");

                await using var scope = await new Builder()
                    .WithinDriver("docker", kernel)
                    .UseCompose(c => c
                        .WithComposeFile("/compose.yml")
                        .WithEnvFile(tempEnvFile)
                        .WithEnvironment("MANUAL_VAR", "manual_value")
                        .WithEnvironment("OVERRIDE_VAR", "manual_override"))
                    .BuildAsync();

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
            var (kernel, mockPack) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");
            mockPack.SetupComposeUpAsync(new ComposeUpResult { ProjectName = "test" });

            try
            {
                await using var scope = await new Builder()
                    .WithinDriver("docker", kernel)
                    .UseCompose(c => c
                        .WithComposeFile("/compose.yml")
                        .WithEnvFile("/non/existent/path/.env")
                        .WithEnvironment("FALLBACK", "value"))
                    .BuildAsync();

                mockPack.ComposeDriver.Verify(d => d.UpAsync(
                    It.IsAny<DriverContext>(),
                    It.Is<ComposeUpConfig>(c =>
                        c.Environment.Count == 1 &&
                        c.Environment.ContainsKey("FALLBACK") &&
                        c.Environment["FALLBACK"] == "value"),
                    It.IsAny<System.Threading.CancellationToken>()), Times.Once);
            }
            finally { kernel.Dispose(); }
        }

        [Fact]
        public async Task WithEnvFile_HandlesQuotedValues()
        {
            var (kernel, mockPack) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");
            mockPack.SetupComposeUpAsync(new ComposeUpResult { ProjectName = "test" });

            var tempEnvFile = Path.GetTempFileName();
            try
            {
                await File.WriteAllTextAsync(tempEnvFile, @"
SIMPLE=simple_value
WITH_SPACES=value with spaces
EQUALS_IN_VALUE=key=value=more
");

                await using var scope = await new Builder()
                    .WithinDriver("docker", kernel)
                    .UseCompose(c => c
                        .WithComposeFile("/compose.yml")
                        .WithEnvFile(tempEnvFile))
                    .BuildAsync();

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
