using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Model.Containers;
using FluentDocker.Services;
using FluentDocker.Services.Extensions;
using Xunit;

namespace FluentDocker.Tests.Drivers.DockerCliDriverTests.ContainerTests
{
    /// <summary>
    /// Custom resolver tests - ported from V2 FluentNetworkTests.cs
    /// </summary>
    [Trait("Category", "Integration")]
    [Trait("Category", "Docker")]
    public class CustomResolverTests : DockerTestBase
    {
        [Fact]
        public async Task CustomResolver_ShouldBeInvoked()
        {
            // Arrange
            var resolverInvoked = false;

            await using var scope = await new Builder()
                .WithinDriver(DriverId, Kernel)
                .UseContainer(c => c
                    .UseImage("nginx:alpine")
                    .ExposePort("80")
                    .UseCustomResolver((ports, portAndProto, uri) =>
                    {
                        resolverInvoked = true;

                        if (ports == null || string.IsNullOrEmpty(portAndProto))
                            return null;

                        if (!ports.TryGetValue(portAndProto, out var endpoints))
                            return null;

                        if (endpoints == null || endpoints.Length == 0)
                            return null;

                        return new IPEndPoint(IPAddress.Loopback, endpoints[0].Port);
                    })
                    .WaitForPort("80/tcp", 30000))
                .BuildAsync();

            var container = GetContainer(scope);
            await container.StartAsync();

            // Act - This should invoke the custom resolver
            var endpoint = await container.ToHostExposedEndpointAsync("80/tcp");

            // Assert
            Assert.True(resolverInvoked, "Custom resolver should have been invoked");
            Assert.NotNull(endpoint);
        }

        [Fact]
        public async Task CustomResolver_WithModifiedEndpoint_ShouldReturnModified()
        {
            // Arrange
            const int customPort = 99999;

            await using var scope = await new Builder()
                .WithinDriver(DriverId, Kernel)
                .UseContainer(c => c
                    .UseImage("nginx:alpine")
                    .ExposePort("80")
                    .UseCustomResolver((ports, portAndProto, uri) =>
                    {
                        if (ports == null || string.IsNullOrEmpty(portAndProto))
                            return null;

                        if (!ports.TryGetValue(portAndProto, out var endpoints))
                            return null;

                        if (endpoints == null || endpoints.Length == 0)
                            return null;

                        // Return modified endpoint with custom port
                        return new IPEndPoint(IPAddress.Loopback, customPort);
                    }))
                .BuildAsync();

            var container = GetContainer(scope);
            await container.StartAsync();

            // Act
            var endpoint = await container.ToHostExposedEndpointAsync("80/tcp");

            // Assert
            Assert.NotNull(endpoint);
            Assert.Equal(customPort, endpoint.Port);
        }

        [Fact]
        public async Task CustomResolver_ReturningNull_ShouldFallbackToDefault()
        {
            // Arrange
            await using var scope = await new Builder()
                .WithinDriver(DriverId, Kernel)
                .UseContainer(c => c
                    .UseImage("nginx:alpine")
                    .ExposePort("80")
                    .UseCustomResolver((ports, portAndProto, uri) =>
                    {
                        // Return null to use default resolution
                        return null;
                    })
                    .WaitForPort("80/tcp", 30000))
                .BuildAsync();

            var container = GetContainer(scope);
            await container.StartAsync();

            // Act - Should fall back to default resolution
            var endpoint = await container.ToHostExposedEndpointAsync("80/tcp");

            // Assert
            Assert.NotNull(endpoint);
            Assert.NotEqual(0, endpoint.Port);
        }
    }
}
