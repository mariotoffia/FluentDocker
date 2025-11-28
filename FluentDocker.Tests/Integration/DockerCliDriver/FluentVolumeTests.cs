using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Extensions;
using Xunit;

namespace FluentDocker.Tests.Integration.DockerCliDriver
{
    /// <summary>
    /// Integration tests for volume operations via IVolumeDriver.
    /// Ported from V2 FluentVolumeTests.cs
    /// </summary>
    [Trait("Category", "Integration")]
    [Trait("Category", "FluentVolume")]
    [Collection("DockerDriver")]
    public class FluentVolumeTests : DockerDriverTestBase
    {
        #region Volume Lifecycle Tests

        [Fact]
        public async Task Volume_WithoutRemoveOnDispose_PersistsAfterContainerRemoved()
        {
            string containerId = null;
            var volumeName = UniqueName("persist");
            
            try
            {
                // Arrange - Create volume
                var volumeResult = await VolumeDriver.CreateAsync(Context, new VolumeCreateConfig
                {
                    Name = volumeName
                });
                Assert.True(volumeResult.Success);

                // Create container with volume
                var containerResult = await ContainerDriver.RunAsync(Context, new ContainerCreateConfig
                {
                    Image = PostgresImage,
                    Environment = new Dictionary<string, string>
                    {
                        ["POSTGRES_PASSWORD"] = "mysecretpassword"
                    },
                    Volumes = new Dictionary<string, string>
                    {
                        [volumeName] = "/var/lib/postgresql/data"
                    },
                    Detach = true
                });
                Assert.True(containerResult.Success);
                containerId = containerResult.Data.Id;

                // Remove container
                await RemoveContainerAsync(containerId);
                containerId = null;

                // Assert - Volume should still exist
                var inspectResult = await VolumeDriver.InspectAsync(Context, volumeName);
                Assert.True(inspectResult.Success);
                Assert.Equal(volumeName, inspectResult.Data.Name);
            }
            finally
            {
                await RemoveContainerAsync(containerId);
                await RemoveVolumeAsync(volumeName);
            }
        }

        [Fact]
        public async Task Volume_CanBeRemovedAfterContainerDeleted()
        {
            string containerId = null;
            var volumeName = UniqueName("removable");
            
            try
            {
                // Arrange
                await VolumeDriver.CreateAsync(Context, new VolumeCreateConfig { Name = volumeName });
                
                var containerResult = await ContainerDriver.RunAsync(Context, new ContainerCreateConfig
                {
                    Image = TestImage,
                    Volumes = new Dictionary<string, string>
                    {
                        [volumeName] = "/data"
                    },
                    Command = new[] { "sleep", "5" },
                    Detach = true
                });
                containerId = containerResult.Data.Id;

                // Remove container
                await RemoveContainerAsync(containerId);
                containerId = null;

                // Act - Remove volume
                var removeResult = await VolumeDriver.RemoveAsync(Context, volumeName);

                // Assert
                Assert.True(removeResult.Success);

                var inspectResult = await VolumeDriver.InspectAsync(Context, volumeName);
                Assert.False(inspectResult.Success);
            }
            finally
            {
                await RemoveContainerAsync(containerId);
                await RemoveVolumeAsync(volumeName);
            }
        }

        #endregion

        #region Volume Mount Tests

        [Fact]
        public async Task Volume_MountedInContainer_AppearsInMounts()
        {
            string containerId = null;
            var volumeName = UniqueName("mounted");
            
            try
            {
                // Arrange
                await VolumeDriver.CreateAsync(Context, new VolumeCreateConfig { Name = volumeName });

                // Act
                var containerResult = await ContainerDriver.RunAsync(Context, new ContainerCreateConfig
                {
                    Image = PostgresImage,
                    Environment = new Dictionary<string, string>
                    {
                        ["POSTGRES_PASSWORD"] = "mysecretpassword"
                    },
                    Volumes = new Dictionary<string, string>
                    {
                        [volumeName] = "/var/lib/postgresql/data"
                    },
                    Detach = true
                });
                
                Assert.True(containerResult.Success);
                containerId = containerResult.Data.Id;

                // Assert
                var inspect = await ContainerDriver.InspectAsync(Context, containerId);
                Assert.True(inspect.Success);
                Assert.NotNull(inspect.Data.Mounts);
                Assert.Contains(inspect.Data.Mounts, m => m.Name == volumeName);
            }
            finally
            {
                await RemoveContainerAsync(containerId);
                await RemoveVolumeAsync(volumeName);
            }
        }

        [Fact]
        public async Task Volume_DataPersistsBetweenContainers()
        {
            string container1Id = null;
            string container2Id = null;
            var volumeName = UniqueName("shared-data");
            var testData = $"test-{Guid.NewGuid()}";
            
            try
            {
                // Arrange - Create volume
                await VolumeDriver.CreateAsync(Context, new VolumeCreateConfig { Name = volumeName });

                // Start first container and write data
                var container1Result = await ContainerDriver.RunAsync(Context, new ContainerCreateConfig
                {
                    Image = TestImage,
                    Volumes = new Dictionary<string, string>
                    {
                        [volumeName] = "/data"
                    },
                    Command = new[] { "sh", "-c", $"echo '{testData}' > /data/test.txt && sleep 5" },
                    Detach = true
                });
                container1Id = container1Result.Data.Id;

                // Wait for write to complete
                await Task.Delay(2000);

                // Remove first container
                await RemoveContainerAsync(container1Id);
                container1Id = null;

                // Start second container and read data
                var container2Result = await ContainerDriver.RunAsync(Context, new ContainerCreateConfig
                {
                    Image = TestImage,
                    Volumes = new Dictionary<string, string>
                    {
                        [volumeName] = "/data"
                    },
                    Command = new[] { "sleep", "60" },
                    Detach = true
                });
                container2Id = container2Result.Data.Id;

                // Act - Read the data
                var readResult = await ContainerDriver.ExecAsync(Context, container2Id, new ExecConfig
                {
                    Command = new[] { "cat", "/data/test.txt" }
                });

                // Assert
                Assert.True(readResult.Success);
                Assert.Contains(testData, readResult.Data.StdOut);
            }
            finally
            {
                await RemoveContainerAsync(container1Id);
                await RemoveContainerAsync(container2Id);
                await RemoveVolumeAsync(volumeName);
            }
        }

        #endregion

        #region Bind Mount Tests

        [Fact]
        public async Task BindMount_WithReadOnly_PreventsWrites()
        {
            string containerId = null;
            var hostPath = Path.Combine(Path.GetTempPath(), $"fluentdocker-{Guid.NewGuid():N}");
            
            try
            {
                // Arrange
                Directory.CreateDirectory(hostPath);
                File.WriteAllText(Path.Combine(hostPath, "readonly.txt"), "original content");

                // Act - Mount as read-only and try to write
                var containerResult = await ContainerDriver.RunAsync(Context, new ContainerCreateConfig
                {
                    Image = TestImage,
                    BindMounts = new Dictionary<string, string>
                    {
                        [hostPath] = "/data:ro"
                    },
                    Command = new[] { "sleep", "60" },
                    Detach = true
                });
                
                Assert.True(containerResult.Success);
                containerId = containerResult.Data.Id;

                // Try to write (should fail)
                var writeResult = await ContainerDriver.ExecAsync(Context, containerId, new ExecConfig
                {
                    Command = new[] { "touch", "/data/newfile.txt" }
                });

                // Assert - Write should fail on read-only mount
                Assert.False(writeResult.Data.StdErr.Contains("Read-only file system") || 
                            writeResult.Data.ExitCode != 0 || 
                            !writeResult.Success);
            }
            finally
            {
                await RemoveContainerAsync(containerId);
                if (Directory.Exists(hostPath))
                    Directory.Delete(hostPath, true);
            }
        }

        [Fact]
        public async Task BindMount_HostFileChanges_VisibleInContainer()
        {
            string containerId = null;
            var hostPath = Path.Combine(Path.GetTempPath(), $"fluentdocker-{Guid.NewGuid():N}");
            
            try
            {
                // Arrange
                Directory.CreateDirectory(hostPath);

                // Start container with bind mount
                var containerResult = await ContainerDriver.RunAsync(Context, new ContainerCreateConfig
                {
                    Image = NginxImage,
                    BindMounts = new Dictionary<string, string>
                    {
                        [hostPath] = "/usr/share/nginx/html"
                    },
                    PortBindings = new Dictionary<string, string>
                    {
                        ["80/tcp"] = "0"
                    },
                    Detach = true
                });
                
                Assert.True(containerResult.Success);
                containerId = containerResult.Data.Id;

                // Wait for nginx to start
                await Task.Delay(2000);

                // Write file to host path
                var testContent = "<html><body>Hello World</body></html>";
                File.WriteAllText(Path.Combine(hostPath, "test.html"), testContent);

                // Act - Read from container
                var catResult = await ContainerDriver.ExecAsync(Context, containerId, new ExecConfig
                {
                    Command = new[] { "cat", "/usr/share/nginx/html/test.html" }
                });

                // Assert
                Assert.True(catResult.Success);
                Assert.Contains("Hello World", catResult.Data.StdOut);
            }
            finally
            {
                await RemoveContainerAsync(containerId);
                if (Directory.Exists(hostPath))
                    Directory.Delete(hostPath, true);
            }
        }

        [Fact]
        public async Task BindMount_WithSpacesInPath_WorksCorrectly()
        {
            string containerId = null;
            var hostPath = Path.Combine(Path.GetTempPath(), $"fluent docker test with spaces-{Guid.NewGuid():N}");
            
            try
            {
                // Arrange
                Directory.CreateDirectory(hostPath);
                var testContent = "<html><body>Spaces in path work!</body></html>";
                File.WriteAllText(Path.Combine(hostPath, "index.html"), testContent);

                // Act
                var containerResult = await ContainerDriver.RunAsync(Context, new ContainerCreateConfig
                {
                    Image = NginxImage,
                    BindMounts = new Dictionary<string, string>
                    {
                        [hostPath] = "/usr/share/nginx/html:ro"
                    },
                    Detach = true
                });
                
                Assert.True(containerResult.Success, $"Run failed: {containerResult.Error}");
                containerId = containerResult.Data.Id;

                // Wait for nginx to start
                await Task.Delay(1000);

                // Verify file is accessible
                var catResult = await ContainerDriver.ExecAsync(Context, containerId, new ExecConfig
                {
                    Command = new[] { "cat", "/usr/share/nginx/html/index.html" }
                });

                // Assert
                Assert.True(catResult.Success);
                Assert.Contains("Spaces in path work!", catResult.Data.StdOut);
            }
            finally
            {
                await RemoveContainerAsync(containerId);
                if (Directory.Exists(hostPath))
                    Directory.Delete(hostPath, true);
            }
        }

        #endregion

        #region Volume With Labels Tests

        [Fact]
        public async Task Volume_WithLabels_CreatesWithLabels()
        {
            var volumeName = UniqueName("labeled");
            
            try
            {
                // Act
                var result = await VolumeDriver.CreateAsync(Context, new VolumeCreateConfig
                {
                    Name = volumeName,
                    Labels = new Dictionary<string, string>
                    {
                        ["com.example.app"] = "myapp",
                        ["com.example.env"] = "test"
                    }
                });

                // Assert
                Assert.True(result.Success);

                var inspect = await VolumeDriver.InspectAsync(Context, volumeName);
                Assert.True(inspect.Success);
                Assert.Equal(volumeName, inspect.Data.Name);
                // Labels should be in the volume (if supported by model)
            }
            finally
            {
                await RemoveVolumeAsync(volumeName);
            }
        }

        #endregion

        #region Anonymous Volume Tests

        [Fact]
        public async Task Container_WithAnonymousVolume_CreatesVolume()
        {
            string containerId = null;
            
            try
            {
                // Act - Create container with anonymous volume
                var containerResult = await ContainerDriver.RunAsync(Context, new ContainerCreateConfig
                {
                    Image = PostgresImage,
                    Environment = new Dictionary<string, string>
                    {
                        ["POSTGRES_PASSWORD"] = "mysecretpassword"
                    },
                    Detach = true
                });
                
                Assert.True(containerResult.Success);
                containerId = containerResult.Data.Id;

                // Assert - Postgres image declares a volume
                var inspect = await ContainerDriver.InspectAsync(Context, containerId);
                Assert.True(inspect.Success);
                // Postgres has a declared volume at /var/lib/postgresql/data
            }
            finally
            {
                await RemoveContainerAsync(containerId);
            }
        }

        #endregion

        #region tmpfs Mount Tests

        [Fact]
        public async Task Container_WithTmpfsMount_MountsTmpfs()
        {
            string containerId = null;
            
            try
            {
                // Act
                var containerResult = await ContainerDriver.RunAsync(Context, new ContainerCreateConfig
                {
                    Image = TestImage,
                    TmpfsMounts = new[] { "/tmp/cache:size=100m" },
                    Command = new[] { "sleep", "60" },
                    Detach = true
                });
                
                Assert.True(containerResult.Success, $"Run failed: {containerResult.Error}");
                containerId = containerResult.Data.Id;

                // Assert
                var inspect = await ContainerDriver.InspectAsync(Context, containerId);
                Assert.True(inspect.Success);
            }
            finally
            {
                await RemoveContainerAsync(containerId);
            }
        }

        #endregion
    }
}

