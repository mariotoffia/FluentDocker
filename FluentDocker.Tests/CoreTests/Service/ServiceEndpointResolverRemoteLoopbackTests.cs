using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Model.Containers;
using FluentDocker.Model.Drivers;
using FluentDocker.Services;
using FluentDocker.Services.Impl;
using FluentDocker.Tests.Mocks;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Service
{
  // S-M1: a literal loopback binding IP (127.0.0.1/::1) published by a REMOTE daemon must not be
  // returned verbatim — that makes the caller probe its OWN loopback instead of the daemon's.
  [Trait("Category", "Unit")]
  public class ServiceEndpointResolverRemoteLoopbackTests : MockKernelTestBase, IAsyncLifetime
  {
    public async ValueTask InitializeAsync()
    {
      await InitializeMockKernelAsync();
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")] // IPAddress.IsLoopback is address-family-agnostic; pin the IPv6 form too.
    public async Task ToHostExposedEndpointAsync_LoopbackBindingRemoteTcpDaemon_ResolvesToDaemonHostNotClientLoopback(
        string loopbackBinding)
    {
      var mockPack = new MockDriverPack();
      SetupPortBinding(mockPack, hostIp: loopbackBinding);
      await using var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync(
          DriverId,
          mockPack,
          new DriverContext(DriverId) { Host = "tcp://192.0.2.10:2376" });
      IContainerService service = new ContainerService(kernel, DriverId, "container-123", "postgres", "db");

      var endpoint = await service.ToHostExposedEndpointAsync(
          "5432/tcp",
          TestContext.Current.CancellationToken);

      Assert.NotEqual(IPAddress.Parse(loopbackBinding), endpoint.Address);
      Assert.Equal(IPAddress.Parse("192.0.2.10"), endpoint.Address);
      Assert.Equal(5432, endpoint.Port);
    }

    [Fact]
    public async Task ToHostExposedEndpointAsync_NonLoopbackBindingRemoteDaemon_ReturnsBindingVerbatim()
    {
      var mockPack = new MockDriverPack();
      SetupPortBinding(mockPack, hostIp: "192.168.1.5");
      await using var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync(
          DriverId,
          mockPack,
          new DriverContext(DriverId) { Host = "tcp://192.0.2.10:2376" });
      IContainerService service = new ContainerService(kernel, DriverId, "container-123", "postgres", "db");

      var endpoint = await service.ToHostExposedEndpointAsync(
          "5432/tcp",
          TestContext.Current.CancellationToken);

      Assert.Equal(IPAddress.Parse("192.168.1.5"), endpoint.Address);
      Assert.Equal(5432, endpoint.Port);
    }

    [Theory]
    [InlineData("unix:///var/run/docker.sock")]
    [InlineData("tcp://localhost:2376")]
    [InlineData("tcp://127.0.0.1:2376")]
    public async Task ToHostExposedEndpointAsync_LoopbackBindingLocalDaemon_ReturnsLoopbackUnchanged(string host)
    {
      var mockPack = new MockDriverPack();
      SetupPortBinding(mockPack, hostIp: "127.0.0.1");
      await using var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync(
          DriverId,
          mockPack,
          new DriverContext(DriverId) { Host = host });
      IContainerService service = new ContainerService(kernel, DriverId, "container-123", "postgres", "db");

      var endpoint = await service.ToHostExposedEndpointAsync(
          "5432/tcp",
          TestContext.Current.CancellationToken);

      Assert.Equal(IPAddress.Loopback, endpoint.Address);
      Assert.Equal(5432, endpoint.Port);
    }

    private static void SetupPortBinding(MockDriverPack mockPack, string hostIp)
    {
      mockPack.ContainerDriver
          .Setup(d => d.InspectAsync(
              It.IsAny<DriverContext>(), "container-123", It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Container>.Ok(new Container
          {
            Id = "container-123",
            NetworkSettings = new ContainerNetworkSettings
            {
              Ports = new Dictionary<string, HostIpEndpoint[]>
              {
                ["5432/tcp"] = [new HostIpEndpoint { HostIp = hostIp, HostPort = "5432" }]
              }
            }
          }));
    }
  }
}
