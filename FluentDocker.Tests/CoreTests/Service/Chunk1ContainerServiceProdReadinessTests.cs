using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
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
  public class Chunk1ContainerServiceProdReadinessTests : MockKernelTestBase, IAsyncLifetime
  {
    public async ValueTask InitializeAsync()
    {
      await InitializeMockKernelAsync();
    }

    [Fact]
    public async Task InspectAsync_WhenLifecycleChangesDuringFetch_DoesNotResurrectStaleState()
    {
      var inspectStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      var inspectResult = new TaskCompletionSource<CommandResponse<Container>>(
          TaskCreationOptions.RunContinuationsAsynchronously);
      MockPack.SetupContainerStop();
      MockPack.ContainerDriver
          .Setup(d => d.InspectAsync(
              It.IsAny<DriverContext>(), "container-123", It.IsAny<CancellationToken>()))
          .Returns<DriverContext, string, CancellationToken>((_, _, _) =>
          {
            inspectStarted.SetResult();
            return inspectResult.Task;
          });
      var service = new ContainerService(
          Kernel, DriverId, "container-123", "alpine", "test",
          initialState: ServiceRunningState.Running);

      var inspectTask = service.InspectAsync(TestContext.Current.CancellationToken);
      await inspectStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
      await service.StopAsync(TestContext.Current.CancellationToken);
      inspectResult.SetResult(CommandResponse<Container>.Ok(new Container
      {
        Id = "container-123",
        State = new ContainerState { Running = true, Status = "running" }
      }));
      await inspectTask;

      Assert.Equal(ServiceRunningState.Stopped, service.State);
    }

    [Fact]
    public async Task InspectAsync_WhenContainerIsGone_MarksStateRemovedBeforeThrowing()
    {
      MockPack.ContainerDriver
          .Setup(d => d.InspectAsync(
              It.IsAny<DriverContext>(), "container-123", It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Container>.Fail(
              "No such container: container-123",
              ErrorCodes.Container.NotFound));
      var service = new ContainerService(
          Kernel, DriverId, "container-123", "alpine", "test",
          initialState: ServiceRunningState.Running);

      await Assert.ThrowsAsync<DriverException>(() =>
          service.InspectAsync(TestContext.Current.CancellationToken));

      Assert.Equal(ServiceRunningState.Removed, service.State);
    }

    [Fact]
    public async Task InspectAsync_WhenNotFoundRacesOlderSuccess_DoesNotResurrectRemovedState()
    {
      var staleInspect = new TaskCompletionSource<CommandResponse<Container>>(
          TaskCreationOptions.RunContinuationsAsynchronously);
      var call = 0;
      MockPack.ContainerDriver
          .Setup(d => d.InspectAsync(
              It.IsAny<DriverContext>(), "container-123", It.IsAny<CancellationToken>()))
          .Returns<DriverContext, string, CancellationToken>((_, _, _) =>
          {
            if (Interlocked.Increment(ref call) == 1)
              return staleInspect.Task;

            return Task.FromResult(CommandResponse<Container>.Fail(
                "No such container: container-123",
                ErrorCodes.Container.NotFound));
          });
      var service = new ContainerService(
          Kernel, DriverId, "container-123", "alpine", "test",
          initialState: ServiceRunningState.Running);

      var stale = service.InspectAsync(TestContext.Current.CancellationToken);
      await Assert.ThrowsAsync<DriverException>(() =>
          service.InspectAsync(TestContext.Current.CancellationToken));
      staleInspect.SetResult(CommandResponse<Container>.Ok(new Container
      {
        Id = "container-123",
        State = new ContainerState { Running = true, Status = "running" }
      }));
      await stale;

      Assert.Equal(ServiceRunningState.Removed, service.State);
    }

    [Fact]
    public async Task InspectAsync_WhenOlderSuccessCompletesAfterNewerSuccess_DoesNotOverwriteNewerState()
    {
      var olderInspect = new TaskCompletionSource<CommandResponse<Container>>(
          TaskCreationOptions.RunContinuationsAsynchronously);
      var call = 0;
      MockPack.ContainerDriver
          .Setup(d => d.InspectAsync(
              It.IsAny<DriverContext>(), "container-123", It.IsAny<CancellationToken>()))
          .Returns<DriverContext, string, CancellationToken>((_, _, _) =>
          {
            if (Interlocked.Increment(ref call) == 1)
              return olderInspect.Task;

            return Task.FromResult(CommandResponse<Container>.Ok(new Container
            {
              Id = "container-123",
              State = new ContainerState { Running = false, Status = "exited" }
            }));
          });
      var service = new ContainerService(Kernel, DriverId, "container-123", "alpine", "test");

      var older = service.InspectAsync(TestContext.Current.CancellationToken);
      await service.InspectAsync(TestContext.Current.CancellationToken);
      olderInspect.SetResult(CommandResponse<Container>.Ok(new Container
      {
        Id = "container-123",
        State = new ContainerState { Running = true, Status = "running" }
      }));
      await older;

      Assert.Equal(ServiceRunningState.Stopped, service.State);
    }

    [Fact]
    public async Task InspectAsync_WhenRunningFlagIsTrue_MapsStateToRunningEvenIfStatusIsExited()
    {
      MockPack.ContainerDriver
          .Setup(d => d.InspectAsync(
              It.IsAny<DriverContext>(), "container-123", It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Container>.Ok(new Container
          {
            Id = "container-123",
            State = new ContainerState { Running = true, Status = "exited" }
          }));
      var service = new ContainerService(Kernel, DriverId, "container-123", "alpine", "test");

      await service.InspectAsync(TestContext.Current.CancellationToken);

      Assert.Equal(ServiceRunningState.Running, service.State);
    }

    [Theory]
    [InlineData("stopped", ServiceRunningState.Stopped)]
    [InlineData("stopping", ServiceRunningState.Stopping)]
    public async Task InspectAsync_MapsPodmanStoppedVocabulary(string status, ServiceRunningState expected)
    {
      MockPack.ContainerDriver
          .Setup(d => d.InspectAsync(
              It.IsAny<DriverContext>(), "container-123", It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Container>.Ok(new Container
          {
            Id = "container-123",
            State = new ContainerState { Running = false, Status = status }
          }));
      var service = new ContainerService(Kernel, DriverId, "container-123", "alpine", "test");

      await service.InspectAsync(TestContext.Current.CancellationToken);

      Assert.Equal(expected, service.State);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_WhenDisposeCleanupTimeoutIsNotPositive_Throws(int milliseconds)
    {
      Assert.Throws<ArgumentOutOfRangeException>(() => new ContainerService(
          Kernel,
          DriverId,
          "container-123",
          "alpine",
          "test",
          disposeCleanupTimeout: TimeSpan.FromMilliseconds(milliseconds)));
    }

    [Fact]
    public void Constructor_WhenDisposeCleanupTimeoutIsInfinite_Throws()
    {
      Assert.Throws<ArgumentOutOfRangeException>(() => new ContainerService(
          Kernel,
          DriverId,
          "container-123",
          "alpine",
          "test",
          disposeCleanupTimeout: Timeout.InfiniteTimeSpan));
    }

    [Fact]
    public async Task StartAsync_WhenContainerWasRemoved_ThrowsInvalidOperationException()
    {
      var service = new ContainerService(
          Kernel, DriverId, "container-123", "alpine", "test",
          initialState: ServiceRunningState.Removed);

      await Assert.ThrowsAsync<InvalidOperationException>(() =>
          service.StartAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PauseAsync_WhenContainerWasRemoved_ThrowsInvalidOperationException()
    {
      var service = new ContainerService(
          Kernel, DriverId, "container-123", "alpine", "test",
          initialState: ServiceRunningState.Removed);

      await Assert.ThrowsAsync<InvalidOperationException>(() =>
          service.PauseAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetLogsAsync_WhenFollowRequested_ThrowsLibraryNotSupportedException()
    {
      var service = new ContainerService(Kernel, DriverId, "container-123", "alpine", "test");

      await Assert.ThrowsAsync<FluentDockerNotSupportedException>(() =>
          service.GetLogsAsync(follow: true, TestContext.Current.CancellationToken));

      MockPack.ContainerDriver.Verify(d => d.GetLogsAsync(
          It.IsAny<DriverContext>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<int?>(),
          It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task WaitForLogMessageAsync_WithContainerService_SearchesFullLogsThenUsesBoundedTail()
    {
      var tails = new System.Collections.Generic.List<int?>();
      MockPack.ContainerDriver
          .SetupSequence(d => d.GetLogsAsync(
              It.IsAny<DriverContext>(), "container-123", false, It.IsAny<int?>(), false,
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<string>.Ok("booting"))
          .ReturnsAsync(CommandResponse<string>.Ok("ready"));
      MockPack.ContainerDriver
          .Setup(d => d.GetLogsAsync(
              It.IsAny<DriverContext>(), "container-123", false, It.IsAny<int?>(), false,
              It.IsAny<CancellationToken>()))
          .Callback<DriverContext, string, bool, int?, bool, CancellationToken>((_, _, _, tail, _, _) =>
              tails.Add(tail))
          .ReturnsAsync(() => CommandResponse<string>.Ok(tails.Count == 1 ? "booting" : "ready"));
      var service = new ContainerService(Kernel, DriverId, "container-123", "alpine", "test");

      var found = await service.WaitForLogMessageAsync(
          "ready",
          timeout: 1000,
          pollIntervalMs: 1,
          cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(found);
      Assert.Equal([null, 100], tails);
    }

    [Fact]
    public async Task WaitForLogMessageAsync_WhenTailMissesReadiness_DoesFinalFullLogScan()
    {
      var tails = new System.Collections.Generic.List<int?>();
      MockPack.ContainerDriver
          .Setup(d => d.GetLogsAsync(
              It.IsAny<DriverContext>(), "container-123", false, It.IsAny<int?>(), false,
              It.IsAny<CancellationToken>()))
          .Callback<DriverContext, string, bool, int?, bool, CancellationToken>((_, _, _, tail, _, _) =>
              tails.Add(tail))
          .ReturnsAsync(() =>
          {
            if (tails.Count == 1)
              return CommandResponse<string>.Ok("booting");
            return CommandResponse<string>.Ok(tails[^1] == null ? "booting\nready" : "noise");
          });
      var streamDriver = new Mock<IStreamDriver>();
      streamDriver
          .Setup(d => d.StreamLogsAsync(
              It.IsAny<DriverContext>(), "container-123", It.IsAny<StreamLogsConfig>(),
              It.IsAny<CancellationToken>()))
          .Returns(StreamLogs("booting", "ready"));
      MockPack.RegisterCustomDriver(streamDriver.Object);
      var service = new ContainerService(Kernel, DriverId, "container-123", "alpine", "test");

      var found = await service.WaitForLogMessageAsync(
          "ready",
          timeout: 20,
          pollIntervalMs: 1,
          cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(found);
      Assert.Contains(tails, tail => tail == 100);
      streamDriver.Verify(d => d.StreamLogsAsync(
          It.IsAny<DriverContext>(),
          "container-123",
          It.Is<StreamLogsConfig>(c => !c.Follow && c.Tail == null),
          It.IsAny<CancellationToken>()), Times.Once);
    }

    private static async IAsyncEnumerable<string> StreamLogs(
        string first,
        string second,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
      await Task.Yield();
      cancellationToken.ThrowIfCancellationRequested();
      yield return first;
      yield return second;
    }
  }
}
