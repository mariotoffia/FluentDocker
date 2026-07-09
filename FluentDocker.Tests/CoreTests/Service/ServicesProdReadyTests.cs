using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Model.Containers;
using FluentDocker.Model.Drivers;
using FluentDocker.Services;
using FluentDocker.Services.Impl;
using FluentDocker.Tests.Mocks;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Service
{
  [Trait("Category", "Unit")]
  public sealed class ServicesProdReadyTests : MockKernelTestBase, IAsyncLifetime
  {
    public async ValueTask InitializeAsync()
    {
      await InitializeMockKernelAsync();
    }

    [Fact]
    public async Task StartAsync_WhenInspectReportsPausedWithRunningFlag_ReportsPaused()
    {
      MockPack.SetupContainerStart();
      SetupContainerInspect(PausedContainer("paused-start"));
      var service = new ContainerService(Kernel, DriverId, "paused-start", "alpine", "paused-start");

      await service.StartAsync(TestContext.Current.CancellationToken);

      Assert.Equal(ServiceRunningState.Paused, service.State);
    }

    [Fact]
    public async Task UnpauseAsync_WhenFallbackInspectReportsPausedWithRunningFlag_ReportsPaused()
    {
      MockPack.ContainerDriver
          .Setup(d => d.UnpauseAsync(
              It.IsAny<DriverContext>(),
              "paused-unpause",
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Fail("container is not paused"));
      SetupContainerInspect(PausedContainer("paused-unpause"));
      var service = new ContainerService(Kernel, DriverId, "paused-unpause", "alpine", "paused-unpause");

      await service.UnpauseAsync(TestContext.Current.CancellationToken);

      Assert.Equal(ServiceRunningState.Paused, service.State);
    }

    [Fact]
    public async Task InspectAsync_WhenInspectReportsPausedWithRunningFlag_ReportsPaused()
    {
      SetupContainerInspect(PausedContainer("paused-inspect"));
      var service = new ContainerService(Kernel, DriverId, "paused-inspect", "alpine", "paused-inspect");

      await service.InspectAsync(TestContext.Current.CancellationToken);

      Assert.Equal(ServiceRunningState.Paused, service.State);
    }

    [Fact]
    public async Task GetContainersAsync_WhenListStatusIsExitedText_ReportsStopped()
    {
      MockPack.SetupContainerList(new Container
      {
        Id = "exited-container",
        Image = "alpine",
        Name = "exited-container",
        State = new ContainerState
        {
          Running = false,
          Status = "Exited (0) 2 minutes ago"
        }
      });
      var host = new HostService(Kernel, DriverId, "host");

      var containers = await host.GetContainersAsync(cancellationToken: TestContext.Current.CancellationToken);

      Assert.Equal(ServiceRunningState.Stopped, containers[0].State);
    }

    [Fact]
    public async Task GetContainersAsync_WhenListStatusIsPausedText_ReportsPaused()
    {
      MockPack.SetupContainerList(new Container
      {
        Id = "paused-container",
        Image = "alpine",
        Name = "paused-container",
        State = new ContainerState
        {
          Running = false,
          Status = "Up 2 minutes (Paused)"
        }
      });
      var host = new HostService(Kernel, DriverId, "host");

      var containers = await host.GetContainersAsync(cancellationToken: TestContext.Current.CancellationToken);

      Assert.Equal(ServiceRunningState.Paused, containers[0].State);
    }

    [Fact]
    public async Task PullImageAsync_WhenImageAndTagBothHaveTags_ThrowsConflict()
    {
      var host = new HostService(Kernel, DriverId, "host");

      var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
          host.PullImageAsync(
              "nginx:1.25",
              tag: "1.26",
              cancellationToken: TestContext.Current.CancellationToken));

      Assert.Equal("tag", ex.ParamName);
      MockPack.ImageDriver.Verify(d => d.PullAsync(
          It.IsAny<DriverContext>(),
          It.IsAny<string>(),
          It.IsAny<string>(),
          It.IsAny<IProgress<ImagePullProgress>>(),
          It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PullImageAsync_WhenUntaggedImageHasExplicitTag_ComposesExpectedReference()
    {
      MockPack.SetupImagePull();
      MockPack.SetupImageInspect("sha256:pulled", "nginx:1.26");
      var host = new HostService(Kernel, DriverId, "host");

      var image = await host.PullImageAsync(
          "nginx",
          tag: "1.26",
          cancellationToken: TestContext.Current.CancellationToken);

      Assert.Equal("nginx:1.26", image.FullName);
      MockPack.ImageDriver.Verify(d => d.PullAsync(
          It.IsAny<DriverContext>(),
          "nginx",
          "1.26",
          It.IsAny<IProgress<ImagePullProgress>>(),
          It.IsAny<CancellationToken>()), Times.Once);
      MockPack.ImageDriver.Verify(d => d.InspectAsync(
          It.IsAny<DriverContext>(),
          "nginx:1.26",
          It.IsAny<CancellationToken>()), Times.Once);
    }

    private void SetupContainerInspect(Container container)
    {
      var id = container.Id ?? throw new ArgumentException("Container id is required.", nameof(container));
      MockPack.ContainerDriver
          .Setup(d => d.InspectAsync(
              It.IsAny<DriverContext>(),
              id,
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Container>.Ok(container));
    }

    private static Container PausedContainer(string id)
    {
      return new Container
      {
        Id = id,
        Image = "alpine",
        Name = id,
        State = new ContainerState
        {
          Running = true,
          Paused = true,
          Status = "paused"
        }
      };
    }
  }
}
