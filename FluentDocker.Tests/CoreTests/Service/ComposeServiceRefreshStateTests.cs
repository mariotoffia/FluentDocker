using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using FluentDocker.Services;
using FluentDocker.Services.Impl;
using FluentDocker.Tests.Mocks;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Service
{
  /// <summary>
  /// Tests for ComposeService.RefreshStateAsync (issue #305): aggregate state is derived
  /// from the live per-service status returned by `docker compose ps`.
  /// </summary>
  [Trait("Category", "Unit")]
  public class ComposeServiceRefreshStateTests
  {
    private static ComposeService CreateService(FluentDockerKernel kernel) =>
        new(kernel, "docker", ["docker-compose.yml"], "test-project");

    private static void SetupList(MockDriverPack pack, params ComposeServiceInfo[] services)
    {
      pack.ComposeDriver
          .Setup(d => d.ListAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<ComposeListConfig>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<IList<ComposeServiceInfo>>.Ok(services));
    }

    [Fact]
    public async Task RefreshStateAsync_AnyRunning_SetsRunning()
    {
      var mockPack = new MockDriverPack();
      SetupList(mockPack,
          new ComposeServiceInfo { Name = "web", State = "running" },
          new ComposeServiceInfo { Name = "db", State = "exited" });

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = CreateService(kernel);
        await service.RefreshStateAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ServiceRunningState.Running, service.State);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task RefreshStateAsync_AllStopped_SetsStopped()
    {
      var mockPack = new MockDriverPack();
      SetupList(mockPack,
          new ComposeServiceInfo { Name = "web", State = "exited" },
          new ComposeServiceInfo { Name = "db", State = "exited" });

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = CreateService(kernel);
        await service.RefreshStateAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ServiceRunningState.Stopped, service.State);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task RefreshStateAsync_NoServices_SetsUnknown()
    {
      var mockPack = new MockDriverPack();
      SetupList(mockPack);

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = CreateService(kernel);
        await service.RefreshStateAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ServiceRunningState.Unknown, service.State);
      }
      finally { kernel.Dispose(); }
    }
  }
}
