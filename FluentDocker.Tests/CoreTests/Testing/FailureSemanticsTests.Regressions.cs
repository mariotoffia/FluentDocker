using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Model.Drivers;
using FluentDocker.Testing.Core;
using FluentDocker.Tests.Mocks;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Testing
{
  public partial class FailureSemanticsTests
  {
    [Fact]
    public async Task DisposeHook_Hangs_CompletesWithinTeardownTimeout()
    {
      MockPack
          .SetupContainerCreate()
          .SetupContainerStart()
          .SetupContainerInspect(running: true)
          .SetupContainerStop()
          .SetupContainerRemove();

      var resource = new ContainerResource(
          Kernel,
          builder => builder.UseImage("alpine:latest"),
          new DockerResourceOptions
          {
            TeardownTimeout = TimeSpan.FromSeconds(2)
          });

      resource.OnBeforeDispose(_ => Task.Delay(Timeout.Infinite));

      await resource.InitializeAsync(TestContext.Current.CancellationToken);

      var disposeTask = resource.DisposeAsync().AsTask();
      var completed = await Task.WhenAny(
          disposeTask,
          Task.Delay(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken));

      Assert.Same(disposeTask, completed);
      await disposeTask; // should not throw
    }

    [Fact]
    public async Task AfterDisposeHook_Hangs_CompletesWithinTeardownTimeout()
    {
      MockPack
          .SetupContainerCreate()
          .SetupContainerStart()
          .SetupContainerInspect(running: true)
          .SetupContainerStop()
          .SetupContainerRemove();

      var resource = new ContainerResource(
          Kernel,
          builder => builder.UseImage("alpine:latest"),
          new DockerResourceOptions
          {
            TeardownTimeout = TimeSpan.FromSeconds(2)
          });

      resource.OnAfterDispose(_ => Task.Delay(Timeout.Infinite));

      await resource.InitializeAsync(TestContext.Current.CancellationToken);

      var disposeTask = resource.DisposeAsync().AsTask();
      var completed = await Task.WhenAny(
          disposeTask,
          Task.Delay(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken));

      Assert.Same(disposeTask, completed);
      await disposeTask;
    }

    [Fact]
    public async Task TeardownFailure_SecondDispose_RetriesTeardown_WhenForceRemoveOff()
    {
      var stopCallCount = 0;

      MockPack
          .SetupContainerCreate()
          .SetupContainerStart()
          .SetupContainerInspect(running: true)
          .SetupContainerRemove();

      // Stop fails on first call, succeeds on second
      MockPack.ContainerDriver
          .Setup(d => d.StopAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              It.IsAny<int?>(),
              It.IsAny<CancellationToken>()))
          .Returns<DriverContext, string, int?, CancellationToken>(
              (_, _, _, _) =>
              {
                stopCallCount++;
                if (stopCallCount == 1)
                  throw new InvalidOperationException("transient stop failure");
                return Task.FromResult(
                    CommandResponse<Unit>.Ok(Unit.Default));
              });

      var resource = new ContainerResource(
          Kernel,
          builder => builder.UseImage("alpine:latest"),
          new DockerResourceOptions { ForceRemoveOnDispose = false });

      await resource.InitializeAsync(TestContext.Current.CancellationToken);

      // First dispose fails (teardown throws, ForceRemoveOnDispose=false)
      await Assert.ThrowsAsync<InvalidOperationException>(
          () => resource.DisposeAsync().AsTask());

      // Second dispose should retry teardown and succeed
      await resource.DisposeAsync();

      Assert.Equal(2, stopCallCount);
    }

    [Fact]
    public async Task InitializeAsync_WhenProvisionedButNotInitialized_ThrowsInvalidOperation()
    {
      MockPack
          .SetupContainerCreate()
          .SetupContainerStart()
          .SetupContainerInspect(running: true)
          .SetupContainerRemove();

      // Make stop throw so teardown fails
      MockPack.ContainerDriver
          .Setup(d => d.StopAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              It.IsAny<int?>(),
              It.IsAny<CancellationToken>()))
          .ThrowsAsync(new InvalidOperationException("stop failure"));

      var resource = new ContainerResource(
          Kernel,
          builder => builder.UseImage("alpine:latest"),
          new DockerResourceOptions { ForceRemoveOnDispose = false });

      await resource.InitializeAsync(TestContext.Current.CancellationToken);

      // Dispose fails — _provisioned stays true, IsInitialized becomes false
      await Assert.ThrowsAsync<InvalidOperationException>(
          () => resource.DisposeAsync().AsTask());

      Assert.False(resource.IsInitialized);

      // Re-init must be blocked to prevent orphaning the old container
      var ex = await Assert.ThrowsAsync<InvalidOperationException>(
          () => resource.InitializeAsync(TestContext.Current.CancellationToken));
      Assert.Contains("provisioned but is not initialized", ex.Message);
    }

    [Fact]
    public async Task Diagnostics_RespectsInitializationTimeout()
    {
      MockPack
          .SetupContainerCreate()
          .SetupContainerStart()
          .SetupContainerInspect(running: true)
          .SetupContainerStop()
          .SetupContainerRemove();

      // Make GetLogsAsync on the driver hang until cancelled
      MockPack.ContainerDriver
          .Setup(d => d.GetLogsAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              It.IsAny<bool>(),
              It.IsAny<int?>(),
              It.IsAny<bool>(),
              It.IsAny<CancellationToken>()))
          .Returns<DriverContext, string, bool, int?, bool, CancellationToken>(
              async (_, _, _, _, _, ct) =>
              {
                await Task.Delay(Timeout.Infinite, ct);
                return default; // unreachable
              });

      var resource = new ContainerResource(
          Kernel,
          builder => builder.UseImage("alpine:latest"),
          new DockerResourceOptions
          {
            InitializationTimeout = TimeSpan.FromSeconds(5),
            CaptureLogsOnFailure = true
          });

      // Hook failure triggers diagnostics after provisioning succeeds
      resource.OnAfterReady(_ =>
          throw new InvalidOperationException("trigger diagnostics"));

      // Should complete within InitializationTimeout, not hang on diagnostics
      var initTask = resource.InitializeAsync(TestContext.Current.CancellationToken);
      var completed = await Task.WhenAny(
          initTask,
          Task.Delay(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken));

      Assert.Same(initTask, completed);
      var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => initTask);
      Assert.Contains("trigger diagnostics", ex.Message);
      Assert.NotNull(resource.Diagnostics);
    }
  }
}
