using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Model.Containers;
using FluentDocker.Model.Drivers;
using FluentDocker.Services.Impl;
using FluentDocker.Tests.Mocks;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Service
{
  // SVC-11: a custom endpoint resolver must be consulted even when the inspect carries no port
  // map (host-network/portless containers publish none) instead of being bypassed by the
  // null-ports short-circuit, which made GetHostPortAsync return 0.
  [Trait("Category", "Unit")]
  public class ServiceEndpointResolverCustomResolverTests : MockKernelTestBase, IAsyncLifetime
  {
    public async ValueTask InitializeAsync()
    {
      await InitializeMockKernelAsync();
    }

    [Fact]
    public async Task ToHostExposedEndpointAsync_NoPortMap_CustomResolverIsStillConsulted()
    {
      SetupInspectWithoutPorts(MockPack);
      var resolverConsulted = false;
      Dictionary<string, HostIpEndpoint[]> seenPorts = null;
      var expected = new IPEndPoint(IPAddress.Loopback, 8080);
      var service = new ContainerService(
          Kernel, DriverId, "container-123", "alpine", "test",
          customResolver: (ports, _, _) =>
          {
            resolverConsulted = true;
            seenPorts = ports;
            return expected;
          });

      var endpoint = await service.ToHostExposedEndpointAsync(
          "8080/tcp", TestContext.Current.CancellationToken);

      Assert.True(resolverConsulted);
      Assert.Null(seenPorts);
      Assert.Equal(expected, endpoint);
    }

    [Fact]
    public async Task GetHostPortAsync_NoPortMapWithCustomResolver_ReturnsResolverPort()
    {
      SetupInspectWithoutPorts(MockPack);
      var service = new ContainerService(
          Kernel, DriverId, "container-123", "alpine", "test",
          customResolver: (_, _, _) => new IPEndPoint(IPAddress.Loopback, 5000));

      var port = await service.GetHostPortAsync("5000/tcp", TestContext.Current.CancellationToken);

      Assert.Equal(5000, port);
    }

    [Fact]
    public async Task ToHostExposedEndpointAsync_NoPortMapNoCustomResolver_ReturnsNull()
    {
      SetupInspectWithoutPorts(MockPack);
      var service = new ContainerService(Kernel, DriverId, "container-123", "alpine", "test");

      var endpoint = await service.ToHostExposedEndpointAsync(
          "8080/tcp", TestContext.Current.CancellationToken);

      Assert.Null(endpoint);
    }

    private static void SetupInspectWithoutPorts(MockDriverPack mockPack)
    {
      mockPack.ContainerDriver
          .Setup(d => d.InspectAsync(
              It.IsAny<DriverContext>(), "container-123", It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Container>.Ok(new Container
          {
            Id = "container-123",
            Name = "test",
            NetworkSettings = new ContainerNetworkSettings { Ports = null }
          }));
    }
  }
}
