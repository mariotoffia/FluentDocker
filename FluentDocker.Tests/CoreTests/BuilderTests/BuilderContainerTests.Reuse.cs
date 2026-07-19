using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Drivers;
using FluentDocker.Model.Containers;
using FluentDocker.Model.Drivers;
using FluentDocker.Services;
using FluentDocker.Tests.Mocks;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.BuilderTests
{
  /// <summary>
  /// Unit tests for <c>ReuseIfExists()</c>: a reused/discovered container is borrowed, so
  /// disposing the wrapper must NOT stop or delete a user's pre-existing container.
  /// </summary>
  public partial class BuilderContainerTests
  {
    [Fact]
    public async Task UseContainer_ReuseExisting_YieldsBorrowedWrapper_DisposeDoesNotStopOrRemove()
    {
      // Arrange — a container with this name already exists and is running.
      MockPack
          .SetupContainerList(new Container { Id = "existing-id", Name = "/reused" })
          .SetupContainerInspect("existing-id", running: true)
          .SetupContainerCreate("existing-id")
          .SetupContainerStart()
          .SetupContainerStop()
          .SetupContainerRemove();

      // Act
      var results = await new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c
              .UseImage("nginx:alpine")
              .WithName("reused")
              .ReuseIfExists())
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);

      // The existing container was reused, not created.
      MockPack.VerifyContainerCreated("nginx:alpine", Times.Never());

      await results.DisposeAllAsync();

      // Borrowed semantics: disposing a reused container must not tear down a user's container.
      MockPack.VerifyContainerStopped(Times.Never());
      MockPack.VerifyContainerRemoved(Times.Never());
    }

    [Fact]
    public async Task ReuseIfExists_WhenStoppedContainerIsStarted_RunsWaitConditions()
    {
      MockPack
          .SetupContainerList(new Container { Id = "existing-id", Name = "/reused" })
          .SetupContainerStart()
          .SetupContainerGetLogs("ready");
      MockPack.ContainerDriver
          .SetupSequence(d => d.InspectAsync(
              It.IsAny<DriverContext>(),
              "existing-id",
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Container>.Ok(new Container
          {
            Id = "existing-id",
            Name = "reused",
            State = new ContainerState { Running = false, Status = "created" }
          }))
          .ReturnsAsync(CommandResponse<Container>.Ok(new Container
          {
            Id = "existing-id",
            Name = "reused",
            State = new ContainerState { Running = true, Status = "running" }
          }));

      await new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c
              .UseImage("nginx:alpine")
              .WithName("reused")
              .ReuseIfExists()
              .WithWaitPollInterval(1)
              .WaitForLogMessage("ready", 50))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);

      MockPack.VerifyContainerStarted(Times.Once());
      MockPack.ContainerDriver.Verify(d => d.GetLogsAsync(
          It.IsAny<DriverContext>(),
          "existing-id",
          false,
          It.IsAny<int?>(),
          false,
          It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReuseIfExists_WhenContainerAlreadyRunning_RunsWaitConditions()
    {
      MockPack
          .SetupContainerList(new Container { Id = "existing-id", Name = "/reused" })
          .SetupContainerInspect("existing-id", running: true)
          .SetupContainerStart()
          .SetupContainerGetLogs("ready");

      await new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c
              .UseImage("nginx:alpine")
              .WithName("reused")
              .ReuseIfExists()
              .WaitForLogMessage("ready", 50))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);

      MockPack.VerifyContainerStarted(Times.Never());
      MockPack.ContainerDriver.Verify(d => d.GetLogsAsync(
          It.IsAny<DriverContext>(),
          "existing-id",
          It.IsAny<bool>(),
          It.IsAny<int?>(),
          It.IsAny<bool>(),
          It.IsAny<CancellationToken>()), Times.Once);
    }
  }
}
