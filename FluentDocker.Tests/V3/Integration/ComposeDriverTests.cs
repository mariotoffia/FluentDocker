using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.V3.Integration
{
    [Trait("Category", "Integration")]
    public class ComposeDriverTests : IDisposable
    {
        private readonly FluentDockerKernel _kernel;
        private readonly IComposeDriver _driver;
        private readonly DriverContext _context;
        private readonly string _testComposeFile;
        private readonly string _testProjectName;

        public ComposeDriverTests()
        {
            _kernel = new KernelBuilder()
                .WithDriver("docker-local", b => b.UseDockerCli())
                .BuildAsync()
                .GetAwaiter()
                .GetResult();

            _driver = _kernel.SysCtl<IComposeDriver>("docker-local");
            _context = new DriverContext("docker-local");
            _testProjectName = $"test-compose-{Guid.NewGuid():N}";

            // Create a temporary compose file for testing
            _testComposeFile = Path.Combine(Path.GetTempPath(), $"docker-compose-{_testProjectName}.yml");
            File.WriteAllText(_testComposeFile, @"
version: '3.8'
services:
  web:
    image: nginx:alpine
    ports:
      - ""8080""
  redis:
    image: redis:alpine
");
        }

        public void Dispose()
        {
            // Cleanup: Stop and remove any running compose services
            try
            {
                var downConfig = new ComposeDownConfig
                {
                    ComposeFiles = new System.Collections.Generic.List<string> { _testComposeFile },
                    ProjectName = _testProjectName,
                    RemoveVolumes = true,
                    RemoveOrphans = true
                };
                _driver.DownAsync(_context, downConfig).GetAwaiter().GetResult();
            }
            catch { }

            // Delete the temporary compose file
            if (File.Exists(_testComposeFile))
            {
                File.Delete(_testComposeFile);
            }

            _kernel?.Dispose();
        }

        [Fact]
        public async Task Compose_Up_StartsServices()
        {
            // Arrange
            var config = new ComposeUpConfig
            {
                ComposeFiles = new System.Collections.Generic.List<string> { _testComposeFile },
                ProjectName = _testProjectName,
                Detached = true
            };

            // Act
            var response = await _driver.UpAsync(_context, config);

            // Assert
            Assert.True(response.Success, response.Error);
            Assert.NotNull(response.Data);
            Assert.Equal(_testProjectName, response.Data.ProjectName);

            // Cleanup
            await _driver.DownAsync(_context, new ComposeDownConfig
            {
                ComposeFiles = new System.Collections.Generic.List<string> { _testComposeFile },
                ProjectName = _testProjectName
            });
        }

        [Fact]
        public async Task Compose_Down_StopsAndRemovesServices()
        {
            // Arrange
            var upConfig = new ComposeUpConfig
            {
                ComposeFiles = new System.Collections.Generic.List<string> { _testComposeFile },
                ProjectName = _testProjectName,
                Detached = true
            };
            await _driver.UpAsync(_context, upConfig);

            var downConfig = new ComposeDownConfig
            {
                ComposeFiles = new System.Collections.Generic.List<string> { _testComposeFile },
                ProjectName = _testProjectName,
                RemoveVolumes = false,
                RemoveOrphans = false
            };

            // Act
            var response = await _driver.DownAsync(_context, downConfig);

            // Assert
            Assert.True(response.Success, response.Error);
        }

        [Fact]
        public async Task Compose_List_ReturnsServices()
        {
            // Arrange
            var upConfig = new ComposeUpConfig
            {
                ComposeFiles = new System.Collections.Generic.List<string> { _testComposeFile },
                ProjectName = _testProjectName,
                Detached = true
            };
            await _driver.UpAsync(_context, upConfig);

            // Act
            var response = await _driver.ListAsync(_context, _testComposeFile, _testProjectName);

            // Assert
            Assert.True(response.Success, response.Error);
            Assert.NotNull(response.Data);
            Assert.NotEmpty(response.Data);

            // Cleanup
            await _driver.DownAsync(_context, new ComposeDownConfig
            {
                ComposeFiles = new System.Collections.Generic.List<string> { _testComposeFile },
                ProjectName = _testProjectName
            });
        }

        [Fact]
        public async Task Compose_Stop_StopsServices()
        {
            // Arrange
            var upConfig = new ComposeUpConfig
            {
                ComposeFiles = new System.Collections.Generic.List<string> { _testComposeFile },
                ProjectName = _testProjectName,
                Detached = true
            };
            await _driver.UpAsync(_context, upConfig);

            // Act
            var response = await _driver.StopAsync(_context, _testComposeFile, timeout: 10);

            // Assert
            Assert.True(response.Success, response.Error);

            // Cleanup
            await _driver.DownAsync(_context, new ComposeDownConfig
            {
                ComposeFiles = new System.Collections.Generic.List<string> { _testComposeFile },
                ProjectName = _testProjectName
            });
        }

        [Fact]
        public async Task Compose_Start_StartsStoppedServices()
        {
            // Arrange
            var upConfig = new ComposeUpConfig
            {
                ComposeFiles = new System.Collections.Generic.List<string> { _testComposeFile },
                ProjectName = _testProjectName,
                Detached = true
            };
            await _driver.UpAsync(_context, upConfig);
            await _driver.StopAsync(_context, _testComposeFile);

            // Act
            var response = await _driver.StartAsync(_context, _testComposeFile);

            // Assert
            Assert.True(response.Success, response.Error);

            // Cleanup
            await _driver.DownAsync(_context, new ComposeDownConfig
            {
                ComposeFiles = new System.Collections.Generic.List<string> { _testComposeFile },
                ProjectName = _testProjectName
            });
        }

        [Fact]
        public async Task Compose_GetLogs_RetrievesLogs()
        {
            // Arrange
            var upConfig = new ComposeUpConfig
            {
                ComposeFiles = new System.Collections.Generic.List<string> { _testComposeFile },
                ProjectName = _testProjectName,
                Detached = true
            };
            await _driver.UpAsync(_context, upConfig);

            // Act
            var response = await _driver.GetLogsAsync(_context, _testComposeFile, follow: false);

            // Assert
            Assert.True(response.Success, response.Error);
            Assert.NotNull(response.Data);

            // Cleanup
            await _driver.DownAsync(_context, new ComposeDownConfig
            {
                ComposeFiles = new System.Collections.Generic.List<string> { _testComposeFile },
                ProjectName = _testProjectName
            });
        }

        [Fact]
        public async Task Compose_Execute_RunsCommand()
        {
            // Arrange
            var upConfig = new ComposeUpConfig
            {
                ComposeFiles = new System.Collections.Generic.List<string> { _testComposeFile },
                ProjectName = _testProjectName,
                Detached = true
            };
            await _driver.UpAsync(_context, upConfig);

            // Act - Execute a simple command in the web service
            var response = await _driver.ExecuteAsync(
                _context,
                _testComposeFile,
                "web",
                new[] { "echo", "test" });

            // Assert
            Assert.True(response.Success, response.Error);
            Assert.NotNull(response.Data);

            // Cleanup
            await _driver.DownAsync(_context, new ComposeDownConfig
            {
                ComposeFiles = new System.Collections.Generic.List<string> { _testComposeFile },
                ProjectName = _testProjectName
            });
        }

        [Fact]
        public async Task Compose_UpWithBuild_BuildsAndStarts()
        {
            // Arrange
            var config = new ComposeUpConfig
            {
                ComposeFiles = new System.Collections.Generic.List<string> { _testComposeFile },
                ProjectName = _testProjectName,
                Detached = true,
                Build = true
            };

            // Act
            var response = await _driver.UpAsync(_context, config);

            // Assert
            Assert.True(response.Success, response.Error);

            // Cleanup
            await _driver.DownAsync(_context, new ComposeDownConfig
            {
                ComposeFiles = new System.Collections.Generic.List<string> { _testComposeFile },
                ProjectName = _testProjectName
            });
        }

        [Fact]
        public async Task Compose_UpWithForceRecreate_RecreatesContainers()
        {
            // Arrange
            var config = new ComposeUpConfig
            {
                ComposeFiles = new System.Collections.Generic.List<string> { _testComposeFile },
                ProjectName = _testProjectName,
                Detached = true,
                ForceRecreate = true
            };

            // Act
            var response = await _driver.UpAsync(_context, config);

            // Assert
            Assert.True(response.Success, response.Error);

            // Cleanup
            await _driver.DownAsync(_context, new ComposeDownConfig
            {
                ComposeFiles = new System.Collections.Generic.List<string> { _testComposeFile },
                ProjectName = _testProjectName
            });
        }

        [Fact]
        public async Task Compose_UpSpecificService_StartsOnlyThatService()
        {
            // Arrange
            var config = new ComposeUpConfig
            {
                ComposeFiles = new System.Collections.Generic.List<string> { _testComposeFile },
                ProjectName = _testProjectName,
                Detached = true,
                Services = new System.Collections.Generic.List<string> { "web" }
            };

            // Act
            var response = await _driver.UpAsync(_context, config);

            // Assert
            Assert.True(response.Success, response.Error);
            Assert.Contains("web", response.Data.Services);

            // Cleanup
            await _driver.DownAsync(_context, new ComposeDownConfig
            {
                ComposeFiles = new System.Collections.Generic.List<string> { _testComposeFile },
                ProjectName = _testProjectName
            });
        }

        [Fact]
        public async Task Compose_DownWithVolumes_RemovesVolumes()
        {
            // Arrange
            var upConfig = new ComposeUpConfig
            {
                ComposeFiles = new System.Collections.Generic.List<string> { _testComposeFile },
                ProjectName = _testProjectName,
                Detached = true
            };
            await _driver.UpAsync(_context, upConfig);

            var downConfig = new ComposeDownConfig
            {
                ComposeFiles = new System.Collections.Generic.List<string> { _testComposeFile },
                ProjectName = _testProjectName,
                RemoveVolumes = true
            };

            // Act
            var response = await _driver.DownAsync(_context, downConfig);

            // Assert
            Assert.True(response.Success, response.Error);
        }

        [Fact]
        public async Task Compose_InvalidComposeFile_Fails()
        {
            // Arrange
            var config = new ComposeUpConfig
            {
                ComposeFiles = new System.Collections.Generic.List<string> { "nonexistent-compose.yml" },
                ProjectName = _testProjectName,
                Detached = true
            };

            // Act
            var response = await _driver.UpAsync(_context, config);

            // Assert
            Assert.False(response.Success);
            Assert.NotNull(response.Error);
        }

        [Fact]
        public async Task Compose_StopWithTimeout_UsesTimeout()
        {
            // Arrange
            var upConfig = new ComposeUpConfig
            {
                ComposeFiles = new System.Collections.Generic.List<string> { _testComposeFile },
                ProjectName = _testProjectName,
                Detached = true
            };
            await _driver.UpAsync(_context, upConfig);

            // Act
            var response = await _driver.StopAsync(_context, _testComposeFile, timeout: 5);

            // Assert
            Assert.True(response.Success, response.Error);

            // Cleanup
            await _driver.DownAsync(_context, new ComposeDownConfig
            {
                ComposeFiles = new System.Collections.Generic.List<string> { _testComposeFile },
                ProjectName = _testProjectName
            });
        }
    }
}
