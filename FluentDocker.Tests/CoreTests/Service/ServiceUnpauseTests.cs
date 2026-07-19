using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Model.Drivers;
using FluentDocker.Services;
using FluentDocker.Services.Impl;
using FluentDocker.Tests.Mocks;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Service
{
  [Trait("Category", "Unit")]
  public class ServiceUnpauseTests : MockKernelTestBase, IAsyncLifetime
  {
    public async ValueTask InitializeAsync()
    {
      await InitializeMockKernelAsync();
    }

    [Fact]
    public async Task ContainerService_UnpauseAsync_CallsDriverAndSetsRunning()
    {
      MockPack.ContainerDriver
          .Setup(d => d.UnpauseAsync(
              It.IsAny<DriverContext>(), "container-123", It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));
      var service = new ContainerService(Kernel, DriverId, "container-123", "alpine", "test");

      await service.UnpauseAsync(TestContext.Current.CancellationToken);

      Assert.Equal(ServiceRunningState.Running, service.State);
      MockPack.ContainerDriver.Verify(d => d.UnpauseAsync(
          It.IsAny<DriverContext>(), "container-123", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ComposeService_UnpauseAsync_CallsDriverAndSetsRunning()
    {
      MockPack.ComposeDriver
          .Setup(d => d.UnpauseAsync(
              It.IsAny<DriverContext>(), It.IsAny<ComposeFileConfig>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));
      var service = new ComposeService(Kernel, DriverId, [], "proj");

      await service.UnpauseAsync(TestContext.Current.CancellationToken);

      Assert.Equal(ServiceRunningState.Running, service.State);
      MockPack.ComposeDriver.Verify(d => d.UnpauseAsync(
          It.IsAny<DriverContext>(),
          It.Is<ComposeFileConfig>(c => c.ProjectName == "proj"),
          It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ComposeService_UnpauseAsync_WhenDriverFails_ThrowsDriverException()
    {
      MockPack.ComposeDriver
          .Setup(d => d.UnpauseAsync(
              It.IsAny<DriverContext>(), It.IsAny<ComposeFileConfig>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Fail("unpause failed", ErrorCodes.Container.UnpauseFailed));
      var service = new ComposeService(Kernel, DriverId, [], "proj");

      await Assert.ThrowsAsync<DriverException>(() =>
          service.UnpauseAsync(TestContext.Current.CancellationToken));
    }
  }
}
