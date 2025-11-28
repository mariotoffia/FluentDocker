using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Extensions;
using Xunit;

namespace FluentDocker.Tests.Integration.DockerCliDriver
{
    /// <summary>
    /// Integration tests for wait conditions.
    /// Ported from V2 WaitTests.cs
    /// </summary>
    [Trait("Category", "Integration")]
    [Trait("Category", "WaitCondition")]
    [Collection("DockerDriver")]
    public class WaitConditionTests : DockerDriverTestBase
    {
        private static readonly HttpClient HttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        #region Wait For Port Tests

        [Fact]
        public async Task WaitForPort_WhenPortOpen_ReturnsQuickly()
        {
            string containerId = null;
            try
            {
                // Arrange - Start nginx (port 80)
                var runResult = await ContainerDriver.RunAsync(Context, new ContainerCreateConfig
                {
                    Image = NginxImage,
                    PortBindings = new Dictionary<string, string>
                    {
                        ["80/tcp"] = "0" // Random port
                    },
                    Detach = true
                });
                
                Assert.True(runResult.Success);
                containerId = runResult.Data.Id;

                // Get the mapped port
                var inspect = await ContainerDriver.InspectAsync(Context, containerId);
                Assert.True(inspect.Success);
                
                var portBinding = inspect.Data.NetworkSettings?.Ports?["80/tcp"];
                Assert.NotNull(portBinding);
                Assert.True(portBinding.Length > 0);
                
                var hostPort = int.Parse(portBinding[0].HostPort);

                // Act - Wait for port to be open
                var isOpen = await WaitForPortAsync("127.0.0.1", hostPort, TimeSpan.FromSeconds(30));

                // Assert
                Assert.True(isOpen, $"Port {hostPort} should be open");
            }
            finally
            {
                await RemoveContainerAsync(containerId);
            }
        }

        [Fact]
        public async Task WaitForPort_PostgresPort_OpensWhenReady()
        {
            string containerId = null;
            try
            {
                // Arrange - Start postgres
                var runResult = await ContainerDriver.RunAsync(Context, new ContainerCreateConfig
                {
                    Image = PostgresImage,
                    Environment = new Dictionary<string, string>
                    {
                        ["POSTGRES_PASSWORD"] = "mysecretpassword"
                    },
                    PortBindings = new Dictionary<string, string>
                    {
                        ["5432/tcp"] = "0" // Random port
                    },
                    Detach = true
                });
                
                Assert.True(runResult.Success);
                containerId = runResult.Data.Id;

                // Get the mapped port
                var inspect = await ContainerDriver.InspectAsync(Context, containerId);
                Assert.True(inspect.Success);
                
                var portBinding = inspect.Data.NetworkSettings?.Ports?["5432/tcp"];
                Assert.NotNull(portBinding);
                var hostPort = int.Parse(portBinding[0].HostPort);

                // Act - Wait for port
                var isOpen = await WaitForPortAsync("127.0.0.1", hostPort, TimeSpan.FromSeconds(30));

                // Assert
                Assert.True(isOpen, $"PostgreSQL port {hostPort} should be open");
            }
            finally
            {
                await RemoveContainerAsync(containerId);
            }
        }

        #endregion

        #region Wait For Process Tests

        [Fact]
        public async Task WaitForProcess_WhenProcessRunning_Returns()
        {
            string containerId = null;
            try
            {
                // Arrange - Start postgres
                var runResult = await ContainerDriver.RunAsync(Context, new ContainerCreateConfig
                {
                    Image = PostgresImage,
                    Environment = new Dictionary<string, string>
                    {
                        ["POSTGRES_PASSWORD"] = "mysecretpassword"
                    },
                    Detach = true
                });
                
                Assert.True(runResult.Success);
                containerId = runResult.Data.Id;

                // Act - Wait for postgres process
                var processFound = await WaitForProcessAsync(containerId, "postgres", TimeSpan.FromSeconds(30));

                // Assert
                Assert.True(processFound, "Postgres process should be running");
            }
            finally
            {
                await RemoveContainerAsync(containerId);
            }
        }

        #endregion

        #region Wait For Http Tests

        [Fact]
        public async Task WaitForHttp_WhenServiceReady_ReturnsSuccess()
        {
            string containerId = null;
            try
            {
                // Arrange - Start nginx
                var runResult = await ContainerDriver.RunAsync(Context, new ContainerCreateConfig
                {
                    Image = NginxImage,
                    PortBindings = new Dictionary<string, string>
                    {
                        ["80/tcp"] = "0"
                    },
                    Detach = true
                });
                
                Assert.True(runResult.Success);
                containerId = runResult.Data.Id;

                // Get the mapped port
                var inspect = await ContainerDriver.InspectAsync(Context, containerId);
                var portBinding = inspect.Data.NetworkSettings?.Ports?["80/tcp"];
                var hostPort = int.Parse(portBinding[0].HostPort);

                // Act - Wait for HTTP
                var url = $"http://127.0.0.1:{hostPort}/";
                var isReady = await WaitForHttpAsync(url, TimeSpan.FromSeconds(30));

                // Assert
                Assert.True(isReady, "Nginx should respond to HTTP requests");
            }
            finally
            {
                await RemoveContainerAsync(containerId);
            }
        }

        [Fact]
        public async Task WaitForHttp_WithCustomValidation_ChecksContent()
        {
            string containerId = null;
            try
            {
                // Arrange - Start nginx
                var runResult = await ContainerDriver.RunAsync(Context, new ContainerCreateConfig
                {
                    Image = NginxImage,
                    PortBindings = new Dictionary<string, string>
                    {
                        ["80/tcp"] = "0"
                    },
                    Detach = true
                });
                
                Assert.True(runResult.Success);
                containerId = runResult.Data.Id;

                // Get the mapped port
                var inspect = await ContainerDriver.InspectAsync(Context, containerId);
                var portBinding = inspect.Data.NetworkSettings?.Ports?["80/tcp"];
                var hostPort = int.Parse(portBinding[0].HostPort);

                // Act - Wait for HTTP with content validation
                var url = $"http://127.0.0.1:{hostPort}/";
                var isReady = await WaitForHttpWithContentAsync(url, "nginx", TimeSpan.FromSeconds(30));

                // Assert
                Assert.True(isReady, "Nginx page should contain 'nginx'");
            }
            finally
            {
                await RemoveContainerAsync(containerId);
            }
        }

        #endregion

        #region Wait For Health Tests

        [Fact]
        public async Task WaitForHealthy_WhenHealthCheckPasses_ReturnsHealthy()
        {
            string containerId = null;
            try
            {
                // Arrange - Start container with healthcheck
                var runResult = await ContainerDriver.RunAsync(Context, new ContainerCreateConfig
                {
                    Image = PostgresImage,
                    Environment = new Dictionary<string, string>
                    {
                        ["POSTGRES_PASSWORD"] = "mysecretpassword"
                    },
                    HealthCmd = new[] { "CMD-SHELL", "pg_isready -U postgres || exit 1" },
                    HealthInterval = "2s",
                    HealthRetries = 10,
                    HealthTimeout = "5s",
                    HealthStartPeriod = "10s",
                    Detach = true
                });
                
                Assert.True(runResult.Success);
                containerId = runResult.Data.Id;

                // Act - Wait for healthy
                var isHealthy = await WaitForHealthyAsync(containerId, TimeSpan.FromSeconds(60));

                // Assert
                Assert.True(isHealthy, "Container should become healthy");
            }
            finally
            {
                await RemoveContainerAsync(containerId);
            }
        }

        #endregion

        #region Custom Lambda Wait Tests

        [Fact]
        public async Task WaitWithLambda_CustomCondition_WaitsUntilTrue()
        {
            string containerId = null;
            try
            {
                // Arrange - Start postgres
                var runResult = await ContainerDriver.RunAsync(Context, new ContainerCreateConfig
                {
                    Image = PostgresImage,
                    Environment = new Dictionary<string, string>
                    {
                        ["POSTGRES_PASSWORD"] = "mysecretpassword"
                    },
                    PortBindings = new Dictionary<string, string>
                    {
                        ["5432/tcp"] = "0"
                    },
                    Detach = true
                });
                
                Assert.True(runResult.Success);
                containerId = runResult.Data.Id;

                // Get port
                var inspect = await ContainerDriver.InspectAsync(Context, containerId);
                var portBinding = inspect.Data.NetworkSettings?.Ports?["5432/tcp"];
                var hostPort = int.Parse(portBinding[0].HostPort);

                // Act - Wait with custom lambda
                var invocationCount = 0;
                var success = await WaitWithConditionAsync(async () =>
                {
                    invocationCount++;
                    
                    // Try to connect to postgres port
                    try
                    {
                        using var client = new TcpClient();
                        await client.ConnectAsync("127.0.0.1", hostPort);
                        return true;
                    }
                    catch
                    {
                        return false;
                    }
                }, TimeSpan.FromSeconds(30));

                // Assert
                Assert.True(success, "Custom condition should have succeeded");
                Assert.True(invocationCount >= 1, "Lambda should have been invoked at least once");
            }
            finally
            {
                await RemoveContainerAsync(containerId);
            }
        }

        [Fact]
        public async Task WaitWithLambda_WithReusedContainer_StillWaits()
        {
            string containerId = null;
            var containerName = UniqueName("reuse");
            
            try
            {
                // Arrange - Create first container
                var runResult = await ContainerDriver.RunAsync(Context, new ContainerCreateConfig
                {
                    Image = PostgresImage,
                    Name = containerName,
                    Environment = new Dictionary<string, string>
                    {
                        ["POSTGRES_PASSWORD"] = "mysecretpassword"
                    },
                    PortBindings = new Dictionary<string, string>
                    {
                        ["5432/tcp"] = "0"
                    },
                    Detach = true
                });
                Assert.True(runResult.Success);
                containerId = runResult.Data.Id;

                // Wait for it to be ready
                var inspect = await ContainerDriver.InspectAsync(Context, containerId);
                var portBinding = inspect.Data.NetworkSettings?.Ports?["5432/tcp"];
                var hostPort = int.Parse(portBinding[0].HostPort);

                // Verify port is open (simulating "reused" container that's already running)
                var isReady = await WaitForPortAsync("127.0.0.1", hostPort, TimeSpan.FromSeconds(30));
                Assert.True(isReady);

                // Act - The container is now "reused" state
                // Wait lambda should still work on a running container
                var lambdaInvoked = false;
                var success = await WaitWithConditionAsync(async () =>
                {
                    lambdaInvoked = true;
                    try
                    {
                        using var client = new TcpClient();
                        await client.ConnectAsync("127.0.0.1", hostPort);
                        return true;
                    }
                    catch
                    {
                        return false;
                    }
                }, TimeSpan.FromSeconds(10));

                // Assert
                Assert.True(lambdaInvoked, "Lambda should have been invoked even for reused container");
                Assert.True(success, "Condition should have succeeded");
            }
            finally
            {
                await RemoveContainerAsync(containerId);
            }
        }

        #endregion

        #region Timeout Tests

        [Fact]
        public async Task WaitForPort_WhenTimeout_ReturnsFalse()
        {
            // Act - Wait for a port that will never open
            var isOpen = await WaitForPortAsync("127.0.0.1", 59999, TimeSpan.FromSeconds(2));

            // Assert
            Assert.False(isOpen, "Should timeout waiting for non-existent port");
        }

        [Fact]
        public async Task WaitForHttp_WhenTimeout_ReturnsFalse()
        {
            // Act - Wait for HTTP on a port that will never respond
            var isReady = await WaitForHttpAsync("http://127.0.0.1:59999/", TimeSpan.FromSeconds(2));

            // Assert
            Assert.False(isReady, "Should timeout waiting for non-existent HTTP endpoint");
        }

        #endregion

        #region Helper Methods

        private async Task<bool> WaitForPortAsync(string host, int port, TimeSpan timeout)
        {
            var endTime = DateTime.UtcNow + timeout;
            
            while (DateTime.UtcNow < endTime)
            {
                try
                {
                    using var client = new TcpClient();
                    var connectTask = client.ConnectAsync(host, port);
                    if (await Task.WhenAny(connectTask, Task.Delay(1000)) == connectTask)
                    {
                        if (client.Connected)
                            return true;
                    }
                }
                catch
                {
                    // Connection failed, retry
                }
                
                await Task.Delay(500);
            }
            
            return false;
        }

        private async Task<bool> WaitForProcessAsync(string containerId, string processName, TimeSpan timeout)
        {
            var endTime = DateTime.UtcNow + timeout;
            
            while (DateTime.UtcNow < endTime)
            {
                var topResult = await ContainerDriver.TopAsync(Context, containerId);
                if (topResult.Success && topResult.Data?.Processes != null)
                {
                    foreach (var process in topResult.Data.Processes)
                    {
                        // Check if any column contains the process name
                        if (process.Values.Any(v => v.Contains(processName, StringComparison.OrdinalIgnoreCase)))
                        {
                            return true;
                        }
                    }
                }
                
                await Task.Delay(500);
            }
            
            return false;
        }

        private async Task<bool> WaitForHttpAsync(string url, TimeSpan timeout)
        {
            var endTime = DateTime.UtcNow + timeout;
            
            while (DateTime.UtcNow < endTime)
            {
                try
                {
                    var response = await HttpClient.GetAsync(url);
                    if (response.IsSuccessStatusCode)
                        return true;
                }
                catch
                {
                    // Request failed, retry
                }
                
                await Task.Delay(500);
            }
            
            return false;
        }

        private async Task<bool> WaitForHttpWithContentAsync(string url, string expectedContent, TimeSpan timeout)
        {
            var endTime = DateTime.UtcNow + timeout;
            
            while (DateTime.UtcNow < endTime)
            {
                try
                {
                    var response = await HttpClient.GetAsync(url);
                    if (response.IsSuccessStatusCode)
                    {
                        var content = await response.Content.ReadAsStringAsync();
                        if (content.Contains(expectedContent, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }
                catch
                {
                    // Request failed, retry
                }
                
                await Task.Delay(500);
            }
            
            return false;
        }

        private async Task<bool> WaitForHealthyAsync(string containerId, TimeSpan timeout)
        {
            var endTime = DateTime.UtcNow + timeout;
            
            while (DateTime.UtcNow < endTime)
            {
                var inspect = await ContainerDriver.InspectAsync(Context, containerId);
                if (inspect.Success && inspect.Data.State?.Health != null)
                {
                    if (inspect.Data.State.Health.Status?.Equals("healthy", StringComparison.OrdinalIgnoreCase) == true)
                        return true;
                }
                
                await Task.Delay(1000);
            }
            
            return false;
        }

        private async Task<bool> WaitWithConditionAsync(Func<Task<bool>> condition, TimeSpan timeout)
        {
            var endTime = DateTime.UtcNow + timeout;
            
            while (DateTime.UtcNow < endTime)
            {
                try
                {
                    if (await condition())
                        return true;
                }
                catch
                {
                    // Condition threw, retry
                }
                
                await Task.Delay(500);
            }
            
            return false;
        }

        #endregion
    }
}

