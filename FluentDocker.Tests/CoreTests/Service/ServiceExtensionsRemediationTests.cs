using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
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
    public async Task WaitForProcessAsync_WhenCallerCancels_ThrowsOperationCanceledException()
    {
      var service = new Mock<IContainerService>();
      service
          .Setup(s => s.ExecuteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
          .ThrowsAsync(new OperationCanceledException());
      using var cts = new CancellationTokenSource();
      await cts.CancelAsync();

      await Assert.ThrowsAsync<OperationCanceledException>(() =>
          service.Object.WaitForProcessAsync("nginx", 30000, cts.Token));
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
  }
}
