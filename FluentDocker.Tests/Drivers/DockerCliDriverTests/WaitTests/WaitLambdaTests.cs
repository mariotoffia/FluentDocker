using System;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Common;
using FluentDocker.Services;
using FluentDocker.Services.Extensions;
using Xunit;

namespace FluentDocker.Tests.Drivers.DockerCliDriverTests.WaitTests
{
    /// <summary>
    /// Wait lambda tests - ported from V2 WaitTests.cs
    /// </summary>
    [Trait("Category", "Integration")]
    [Trait("Category", "Docker")]
    public class WaitLambdaTests : DockerTestBase
    {
        [Fact]
        public async Task WaitLambda_ShouldGetInvoked()
        {
            // Arrange
            var checkInvoked = false;

            await using var scope = await new Builder()
                .WithinDriver(DriverId, Kernel)
                .UseContainer(c => c
                    .UseImage("nginx:alpine")
                    .ExposePort("80")
                    .Wait((service, count) =>
                    {
                        checkInvoked = true;
                        // Simple check - just succeed after first invocation
                        return 0; // Success
                    }))
                .BuildAsync();

            var container = GetContainer(scope);
            await container.StartAsync();

            // Assert
            Assert.True(checkInvoked, "Wait lambda should have been invoked");
            Assert.Equal(ServiceRunningState.Running, container.State);
        }

        [Fact]
        public async Task WaitLambda_WithRetries_ShouldRetryUntilSuccess()
        {
            // Arrange
            var retryCount = 0;

            await using var scope = await new Builder()
                .WithinDriver(DriverId, Kernel)
                .UseContainer(c => c
                    .UseImage("nginx:alpine")
                    .ExposePort("80")
                    .Wait((service, count) =>
                    {
                        retryCount = count;
                        if (count < 3)
                            return 100; // Retry in 100ms
                        return 0; // Success on 3rd try
                    }))
                .BuildAsync();

            var container = GetContainer(scope);
            await container.StartAsync();

            // Assert
            Assert.True(retryCount >= 3, $"Expected at least 3 retries, got {retryCount}");
        }

        [Fact]
        public async Task WaitLambda_ThatThrows_ShouldPropagateException()
        {
            // Arrange & Act & Assert
            await Assert.ThrowsAsync<FluentDockerException>(async () =>
            {
                await using var scope = await new Builder()
                    .WithinDriver(DriverId, Kernel)
                    .UseContainer(c => c
                        .UseImage("nginx:alpine")
                        .ExposePort("80")
                        .Wait((service, count) =>
                        {
                            if (count > 2)
                                throw new FluentDockerException("Wait condition failed after max retries");
                            return 100;
                        }))
                    .BuildAsync();

                var container = GetContainer(scope);
                await container.StartAsync();
            });
        }

        [Fact]
        public async Task WaitLambda_WithHttpCheck_ShouldWork()
        {
            // Arrange
            await using var scope = await new Builder()
                .WithinDriver(DriverId, Kernel)
                .UseContainer(c => c
                    .UseImage("nginx:alpine")
                    .ExposePort("80")
                    .Wait((service, count) =>
                    {
                        try
                        {
                            var task = service.ToHostExposedEndpointAsync("80/tcp");
                            task.Wait();
                            var endpoint = task.Result;
                            if (endpoint == null) return 500;

                            using var httpClient = new System.Net.Http.HttpClient();
                            httpClient.Timeout = TimeSpan.FromSeconds(2);
                            var response = httpClient.GetAsync($"http://localhost:{endpoint.Port}/").Result;
                            return response.IsSuccessStatusCode ? 0 : 500;
                        }
                        catch
                        {
                            return 500;
                        }
                    }))
                .BuildAsync();

            var container = GetContainer(scope);
            await container.StartAsync();

            // Assert
            Assert.Equal(ServiceRunningState.Running, container.State);
        }
    }
}
