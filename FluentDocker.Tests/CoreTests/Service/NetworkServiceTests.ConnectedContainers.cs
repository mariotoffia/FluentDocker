using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Networks;
using FluentDocker.Services.Impl;
using FluentDocker.Tests.Mocks;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Service
{
  [Trait("Category", "Unit")]
  public partial class NetworkServiceTests
  {
    [Fact]
    public async Task GetConnectedContainersAsync_ReturnsContainerNamesAndFallsBackToIds()
    {
      var mockPack = new MockDriverPack();
      mockPack.NetworkDriver
          .Setup(d => d.InspectAsync(
              It.IsAny<DriverContext>(),
              "net123",
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Network>.Ok(new Network
          {
            Id = "net123",
            Name = "my-network",
            Containers = new Dictionary<string, NetworkedContainer>
            {
              ["aabbcc"] = new NetworkedContainer { Name = "web" },
              ["ddeeff"] = new NetworkedContainer()
            }
          }));

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      var service = new NetworkService(kernel, "docker", "net123", "my-network");

      try
      {
        var containers = await service.GetConnectedContainersAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["web", "ddeeff"], containers);
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    public async Task GetConnectedContainersAsync_WhenInspectFails_ThrowsDriverException()
    {
      var mockPack = new MockDriverPack();
      mockPack.NetworkDriver
          .Setup(d => d.InspectAsync(
              It.IsAny<DriverContext>(),
              "missing",
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Network>.Fail(
              "network not found",
              ErrorCodes.Network.NotFound));

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      var service = new NetworkService(kernel, "docker", "missing", "missing-network");

      try
      {
        var ex = await Assert.ThrowsAsync<DriverException>(
            async () => await service.GetConnectedContainersAsync(TestContext.Current.CancellationToken));

        Assert.Contains("network not found", ex.Message);
      }
      finally
      {
        kernel.Dispose();
      }
    }
  }
}
