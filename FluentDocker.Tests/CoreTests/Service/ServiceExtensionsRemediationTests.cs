using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Model.Containers;
using FluentDocker.Model.Drivers;
using FluentDocker.Services;
using FluentDocker.Services.Extensions;
using FluentDocker.Services.Impl;
using FluentDocker.Tests.Mocks;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Service
{
  [Trait("Category", "Unit")]
  public class ServiceExtensionsRemediationTests : MockKernelTestBase, IAsyncLifetime
  {
    public async ValueTask InitializeAsync()
    {
      await InitializeMockKernelAsync();
    }

    [Fact]
    public async Task GetConfigurationAsync_FreshTrue_BypassesContainerInspectCache()
    {
      MockPack.ContainerDriver
          .SetupSequence(d => d.InspectAsync(
              It.IsAny<DriverContext>(), "container-123", It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Container>.Ok(new Container
          {
            Id = "container-123",
            Name = "first",
            State = new ContainerState { Running = true, Status = "running" }
          }))
          .ReturnsAsync(CommandResponse<Container>.Ok(new Container
          {
            Id = "container-123",
            Name = "second",
            State = new ContainerState { Running = true, Status = "running" }
          }));
      var service = new ContainerService(Kernel, DriverId, "container-123", "alpine", "test");

      var first = await service.GetConfigurationAsync(
          cancellationToken: TestContext.Current.CancellationToken);
      var cached = await service.GetConfigurationAsync(
          cancellationToken: TestContext.Current.CancellationToken);
      var fresh = await service.GetConfigurationAsync(
          fresh: true,
          cancellationToken: TestContext.Current.CancellationToken);

      Assert.Equal("first", first.Name);
      Assert.Equal("first", cached.Name);
      Assert.Equal("second", fresh.Name);
    }

    [Fact]
    public void GetDockerHost_NonNativeTcpHost_ReturnsConfiguredHost()
    {
      var host = new Mock<IHostService>();
      host.Setup(s => s.IsNative).Returns(false);
      host.Setup(s => s.Name).Returns("tcp://192.0.2.10:2376");

      Assert.Equal("192.0.2.10", host.Object.GetDockerHost());
    }

    [Fact]
    public async Task ToHostExposedEndpointAsync_InterfaceTypedContainerService_UsesCustomResolver()
    {
      MockPack.ContainerDriver
          .Setup(d => d.InspectAsync(
              It.IsAny<DriverContext>(), "container-123", It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Container>.Ok(new Container
          {
            Id = "container-123",
            NetworkSettings = new ContainerNetworkSettings
            {
              Ports = new Dictionary<string, HostIpEndpoint[]>
              {
                ["80/tcp"] = [new HostIpEndpoint { HostIp = "0.0.0.0", HostPort = "8080" }]
              }
            }
          }));
      IContainerService service = new ContainerService(
          Kernel,
          DriverId,
          "container-123",
          "nginx",
          "web",
          customResolver: (_, _, _) => new IPEndPoint(IPAddress.Parse("198.51.100.10"), 18080));

      var endpoint = await service.ToHostExposedEndpointAsync(
          "80/tcp",
          TestContext.Current.CancellationToken);

      Assert.Equal(IPAddress.Parse("198.51.100.10"), endpoint.Address);
      Assert.Equal(18080, endpoint.Port);
    }

    [Fact]
    public async Task ToHostExposedEndpointAsync_RemoteWildcardBinding_UsesDriverContextHost()
    {
      var mockPack = new MockDriverPack();
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
                ["80/tcp"] = [new HostIpEndpoint { HostIp = "0.0.0.0", HostPort = "8080" }]
              }
            }
          }));
      await using var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync(
          DriverId,
          mockPack,
          new DriverContext(DriverId) { Host = "tcp://192.0.2.10:2376" });
      IContainerService service = new ContainerService(kernel, DriverId, "container-123", "nginx", "web");

      var endpoint = await service.ToHostExposedEndpointAsync(
          "80/tcp",
          TestContext.Current.CancellationToken);

      Assert.Equal(IPAddress.Parse("192.0.2.10"), endpoint.Address);
      Assert.Equal(8080, endpoint.Port);
    }

    [Fact]
    public async Task ToHostExposedEndpointAsync_WhenDnsFails_DoesNotPoisonResolverCache()
    {
      var resolver = typeof(ServiceExtensions).Assembly.GetType(
          "FluentDocker.Services.Extensions.ServiceEndpointResolver")!;
      var cache = resolver.GetField(
          "DockerHostAddressCache",
          BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
      cache.GetType().GetMethod("Clear")!.Invoke(cache, []);
      var host = $"missing-{Guid.NewGuid():N}.invalid";
      var uri = new Uri($"tcp://{host}:2376");
      var method = resolver.GetMethod(
          "ResolveDockerHostAddressAsync",
          BindingFlags.NonPublic | BindingFlags.Static)!;

      var error = await Assert.ThrowsAnyAsync<Exception>(async () =>
          await (Task<IPAddress>)method.Invoke(null, [uri, TestContext.Current.CancellationToken])!);

      Assert.NotNull(error);
      Assert.False((bool)cache.GetType().GetMethod("ContainsKey")!.Invoke(cache, [host])!);
    }

    [Fact]
    public async Task ToHostExposedEndpointAsync_UnboundPort_ReturnsNull()
    {
      var container = new Container
      {
        Id = "container-123",
        NetworkSettings = new ContainerNetworkSettings
        {
          Ports = new Dictionary<string, HostIpEndpoint[]>
          {
            ["80/tcp"] = [new HostIpEndpoint { HostIp = "0.0.0.0", HostPort = "" }]
          }
        }
      };
      var service = new Mock<IContainerService>();
      service.Setup(s => s.InspectAsync(It.IsAny<CancellationToken>())).ReturnsAsync(container);

      var endpoint = await service.Object.ToHostExposedEndpointAsync(
          "80/tcp",
          TestContext.Current.CancellationToken);

      Assert.Null(endpoint);
    }

    [Fact]
    public async Task WaitForPortAsync_WhenCallerCancels_ThrowsOperationCanceledException()
    {
      using var cts = new CancellationTokenSource();
      await cts.CancelAsync();

      await Assert.ThrowsAsync<OperationCanceledException>(() =>
          ServiceExtensions.WaitForPortAsync("127.0.0.1", 1, 30000, cts.Token));
    }

    [Fact]
    public async Task WaitForPortAsync_PortBindingAppearsLate_ReResolvesEndpoint()
    {
      var service = new Mock<IContainerService>();
      service.Setup(s => s.Id).Returns("container-123");
      using var listener = new TcpListener(IPAddress.Loopback, 0);
      listener.Start();
      var endpoint = (IPEndPoint)listener.LocalEndpoint!;
      service
          .SetupSequence(s => s.ToHostExposedEndpointAsync(
              "80/tcp", It.IsAny<CancellationToken>()))
          .ReturnsAsync((IPEndPoint)null!)
          .ReturnsAsync(endpoint);

      var ready = await service.Object.WaitForPortAsync(
          "80/tcp",
          timeout: 1000,
          pollIntervalMs: 10,
          cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(ready);
      service.Verify(s => s.ToHostExposedEndpointAsync(
          "80/tcp", It.IsAny<CancellationToken>()), Times.AtLeast(2));
    }

    [Fact]
    public async Task WaitForPortAsync_TransientDriverException_ReturnsFalseOnTimeout()
    {
      var service = new Mock<IContainerService>();
      service
          .Setup(s => s.ToHostExposedEndpointAsync("80/tcp", It.IsAny<CancellationToken>()))
          .ThrowsAsync(new DriverException("daemon reconnecting", ErrorCodes.Api.ConnectionFailed));

      var ready = await service.Object.WaitForPortAsync(
          "80/tcp",
          timeout: 5,
          pollIntervalMs: 1,
          cancellationToken: TestContext.Current.CancellationToken);

      Assert.False(ready);
    }

    [Fact]
    public async Task ToHostExposedEndpointAsync_WhenDockerHostContextCannotBeRead_Throws()
    {
      MockPack.SetupContainerStart();
      MockPack.ContainerDriver
          .Setup(d => d.InspectAsync(
              It.IsAny<DriverContext>(), "container-123", It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Container>.Ok(new Container
          {
            Id = "container-123",
            State = new ContainerState { Running = true, Status = "running" },
            NetworkSettings = new ContainerNetworkSettings
            {
              Ports = new Dictionary<string, HostIpEndpoint[]>
              {
                ["80/tcp"] = [new HostIpEndpoint { HostIp = "0.0.0.0", HostPort = "8080" }]
              }
            }
          }));
      var service = new ContainerService(Kernel, DriverId, "container-123", "nginx", "web");
      await service.StartAsync(TestContext.Current.CancellationToken);
      await Kernel.DisposeAsync();

      await Assert.ThrowsAsync<ObjectDisposedException>(() =>
          service.ToHostExposedEndpointAsync("80/tcp", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WaitForHttpAsync_WithIpv6Endpoint_UsesBracketedUrl()
    {
      using var listener = new TcpListener(IPAddress.IPv6Loopback, 0);
      listener.Start();
      var port = ((IPEndPoint)listener.LocalEndpoint).Port;
      var server = Task.Run(async () =>
      {
        using var client = await listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        await reader.ReadLineAsync(TestContext.Current.CancellationToken);
        var bytes = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n");
        await stream.WriteAsync(bytes, TestContext.Current.CancellationToken);
      }, TestContext.Current.CancellationToken);
      var service = new Mock<IContainerService>();
      service
          .Setup(s => s.ToHostExposedEndpointAsync("80/tcp", It.IsAny<CancellationToken>()))
          .ReturnsAsync(new IPEndPoint(IPAddress.IPv6Loopback, port));

      var ready = await service.Object.WaitForHttpAsync(
          "80/tcp",
          "/health",
          timeout: 1000,
          pollIntervalMs: 10,
          cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(ready);
      await server;
    }

    [Fact]
    public async Task WaitForProcessAsync_WhenCallerCancels_ThrowsOperationCanceledException()
    {
      var service = new Mock<IContainerService>();
      service
          .Setup(s => s.ExecuteAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
          .ThrowsAsync(new OperationCanceledException());
      using var cts = new CancellationTokenSource();
      await cts.CancelAsync();

      await Assert.ThrowsAsync<OperationCanceledException>(() =>
          service.Object.WaitForProcessAsync("nginx", 30000, cts.Token));
    }

    [Fact]
    public async Task WaitForProcessAsync_UsesArgumentVectorForPgrep()
    {
      var service = new Mock<IContainerService>();
      string[]? captured = null;
      service
          .Setup(s => s.ExecuteAsync(
              It.IsAny<string[]>(),
              It.IsAny<CancellationToken>()))
          .Callback<string[], CancellationToken>((cmd, _) => captured = cmd)
          .ReturnsAsync("123");

      var ready = await service.Object.WaitForProcessAsync(
          "my app",
          timeout: 1000,
          pollIntervalMs: 1,
          cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(ready);
      Assert.NotNull(captured);
      Assert.Equal(["pgrep", "-f", "my app"], captured);
    }

    [Fact]
    public async Task WaitForProcessAsync_NonTransientDriverException_RethrowsImmediately()
    {
      var service = new Mock<IContainerService>();
      service
          .Setup(s => s.ExecuteAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
          .ThrowsAsync(new DriverException("bad command", ErrorCodes.Container.ExecFailed));

      await Assert.ThrowsAsync<DriverException>(() =>
          service.Object.WaitForProcessAsync(
              "nginx",
              timeout: 1000,
              pollIntervalMs: 1,
              cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WaitForLogMessageAsync_WhenCallerCancels_ThrowsOperationCanceledException()
    {
      var service = new Mock<IContainerService>();
      service
          .Setup(s => s.GetLogsAsync(false, It.IsAny<CancellationToken>()))
          .ThrowsAsync(new OperationCanceledException());
      using var cts = new CancellationTokenSource();
      await cts.CancelAsync();

      await Assert.ThrowsAsync<OperationCanceledException>(() =>
          service.Object.WaitForLogMessageAsync("ready", 30000, cts.Token));
    }

    [Fact]
    public async Task WaitForLogMessageAsync_NonTransientDriverException_RethrowsImmediately()
    {
      var service = new Mock<IContainerService>();
      service
          .Setup(s => s.GetLogsAsync(false, It.IsAny<CancellationToken>()))
          .ThrowsAsync(new DriverException("logs unavailable", ErrorCodes.Container.LogsFailed));

      await Assert.ThrowsAsync<DriverException>(() =>
          service.Object.WaitForLogMessageAsync(
              "ready",
              timeout: 1000,
              pollIntervalMs: 1,
              cancellationToken: TestContext.Current.CancellationToken));
    }
  }
}
