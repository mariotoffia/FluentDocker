using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Model.Containers;
using FluentDocker.Model.Drivers;
using FluentDocker.Tests.Mocks;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.BuilderTests
{
  public partial class BuilderContainerTests
  {
    [Fact]
    public async Task BuildAsync_CalledTwiceAfterSuccess_ThrowsAlreadyConsumed()
    {
      MockPack
          .SetupContainerCreate()
          .SetupContainerStart()
          .SetupContainerInspect(running: true)
          .SetupContainerStop()
          .SetupContainerRemove();
      var builder = new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c.UseImage("alpine"));

      await using var results = await builder.BuildAsync(
          cancellationToken: TestContext.Current.CancellationToken);

      var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
          builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken));
      Assert.Equal("builder already consumed by BuildAsync; create a new Builder", ex.Message);
    }

    [Fact]
    public async Task BuildAsync_WhenFirstAttemptFails_AllowsRetry()
    {
      MockPack
          .SetupContainerStart()
          .SetupContainerInspect("retry-container", running: true)
          .SetupContainerStop()
          .SetupContainerRemove();
      MockPack.ContainerDriver
          .SetupSequence(d => d.CreateAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<ContainerCreateConfig>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<ContainerCreateResult>.Fail(
              "create failed", ErrorCodes.Container.CreateFailed))
          .ReturnsAsync(CommandResponse<ContainerCreateResult>.Ok(
              new ContainerCreateResult { Id = "retry-container" }));
      var builder = new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c.UseImage("alpine"));

      await Assert.ThrowsAsync<DriverException>(() =>
          builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken));
      await using var results = await builder.BuildAsync(
          cancellationToken: TestContext.Current.CancellationToken);

      Assert.Single(results.All);
      MockPack.ContainerDriver.Verify(d => d.CreateAsync(
          It.IsAny<DriverContext>(),
          It.IsAny<ContainerCreateConfig>(),
          It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task BuildAsync_RetryAfterWaitFailure_RerunsWaitConditions()
    {
      var createAttempts = 0;
      var logAttempts = 0;
      MockPack
          .SetupContainerCreate("retry-container")
          .SetupContainerStart()
          .SetupContainerInspect("retry-container", running: true)
          .SetupContainerRemove();
      MockPack.ContainerDriver
          .Setup(d => d.CreateAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<ContainerCreateConfig>(),
              It.IsAny<CancellationToken>()))
          .Returns(() =>
          {
            createAttempts++;
            return Task.FromResult(CommandResponse<ContainerCreateResult>.Ok(
                new ContainerCreateResult { Id = "retry-container" }));
          });
      MockPack.ContainerDriver
          .Setup(d => d.GetLogsAsync(
              It.IsAny<DriverContext>(), "retry-container", It.IsAny<bool>(),
              It.IsAny<int?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
          .Returns(() => Task.FromResult(CommandResponse<string>.Ok(
              createAttempts == 1 ? $"pending {++logAttempts}" : "ready")));
      var builder = new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c
              .UseImage("alpine")
              .WithWaitPollInterval(1)
              .WaitForLogMessage("ready", 5));

      await Assert.ThrowsAsync<FluentDockerException>(() =>
          builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken));
      await using var results = await builder.BuildAsync(
          cancellationToken: TestContext.Current.CancellationToken);

      Assert.Single(results.All);
      Assert.Equal(2, createAttempts);
      Assert.True(logAttempts > 0);
    }

    [Fact]
    public async Task BuildAsync_WhenCanceledDuringPostCreateStep_RemovesContainerThenRethrows()
    {
      var enteredCopy = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      MockPack
          .SetupContainerCreate("cancel-container")
          .SetupContainerStart()
          .SetupContainerInspect("cancel-container", running: true)
          .SetupContainerRemove()
          .SetupContainerGetLogs("tail");
      MockPack.ContainerDriver
          .Setup(d => d.CopyToAsync(
              It.IsAny<DriverContext>(), "cancel-container", It.IsAny<string>(),
              It.IsAny<string>(), It.IsAny<CancellationToken>()))
          .Returns<DriverContext, string, string, string, CancellationToken>(async (_, _, _, _, ct) =>
          {
            enteredCopy.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct).ConfigureAwait(false);
            return CommandResponse<Unit>.Ok(Unit.Default);
          });
      await File.WriteAllTextAsync(
          Path.Combine(".out", "cancel-copy-source.txt"), "data",
          TestContext.Current.CancellationToken);
      using var cts = CancellationTokenSource.CreateLinkedTokenSource(
          TestContext.Current.CancellationToken);
      var build = new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c
              .UseImage("alpine")
              .CopyToOnStart(Path.Combine(".out", "cancel-copy-source.txt"), "/data"))
          .BuildAsync(cancellationToken: cts.Token);
      await enteredCopy.Task.WaitAsync(TestContext.Current.CancellationToken);
      cts.Cancel();

      await Assert.ThrowsAnyAsync<OperationCanceledException>(() => build);
      MockPack.ContainerDriver.Verify(d => d.RemoveAsync(
          It.IsAny<DriverContext>(), "cancel-container", true, true,
          It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task WaitFailure_WithKeepContainer_DoesNotRemoveContainer()
    {
      MockPack
          .SetupContainerCreate("container-123")
          .SetupContainerStart()
          .SetupContainerInspect("container-123", running: true)
          .SetupContainerRemove()
          .SetupContainerGetLogs("tail log");

      await Assert.ThrowsAsync<FluentDockerException>(() => new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c
              .UseImage("alpine")
              .KeepContainer()
              .WithWaitPollInterval(1)
              .WaitForLogMessage("never", 5))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken));

      MockPack.VerifyContainerRemoved(Times.Never());
    }

    [Fact]
    public async Task WaitFailure_DefaultCleanup_RemovesThroughServiceWithHooksVolumesAndLogs()
    {
      MockPack
          .SetupContainerCreate("container-123")
          .SetupContainerStart()
          .SetupContainerInspect("container-123", running: true)
          .SetupContainerRemove()
          .SetupContainerCopyFrom()
          .SetupContainerGetLogs("important tail");

      var ex = await Assert.ThrowsAsync<FluentDockerException>(() => new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c
              .UseImage("alpine")
              .CopyFromOnDispose("/logs", ".out/wait-failure-logs")
              .WithWaitPollInterval(1)
              .WaitForLogMessage("never", 5))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken));

      Assert.Contains("important tail", ex.Message);
      MockPack.ContainerDriver.Verify(d => d.CopyFromAsync(
          It.IsAny<DriverContext>(),
          "container-123",
          "/logs",
          ".out/wait-failure-logs",
          It.IsAny<CancellationToken>()), Times.Once);
      MockPack.ContainerDriver.Verify(d => d.RemoveAsync(
          It.IsAny<DriverContext>(),
          "container-123",
          true,
          true,
          It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task WaitForProcess_UsesConfiguredPollInterval()
    {
      MockPack
          .SetupContainerCreate("container-123")
          .SetupContainerStart()
          .SetupContainerInspect("container-123", running: true)
          .SetupContainerStop()
          .SetupContainerRemove();
      MockPack.ContainerDriver
          .SetupSequence(d => d.ExecAsync(
              It.IsAny<DriverContext>(),
              "container-123",
              It.IsAny<ExecConfig>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<ExecResult>.Ok(new ExecResult { StdOut = "" }))
          .ReturnsAsync(CommandResponse<ExecResult>.Ok(new ExecResult { StdOut = "123" }));

      await using var results = await new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c
              .UseImage("alpine")
              .WithWaitPollInterval(1)
              .WaitForProcess("nginx", 50))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);

      MockPack.ContainerDriver.Verify(d => d.ExecAsync(
          It.IsAny<DriverContext>(),
          "container-123",
          It.IsAny<ExecConfig>(),
          It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task WaitForPort_WithAddressAndUnexposedPort_HasNotExposedMessage()
    {
      MockPack
          .SetupContainerCreate("container-123")
          .SetupContainerStart()
          .SetupContainerInspect("container-123", running: true)
          .SetupContainerRemove()
          .SetupContainerGetLogs("");

      var ex = await Assert.ThrowsAsync<FluentDockerException>(() => new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c
              .UseImage("alpine")
              .WithWaitPollInterval(1)
              .WaitForPort("9999/tcp", "127.0.0.1", 5))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken));

      Assert.Contains("not exposed", ex.Message);
    }

    [Fact]
    public async Task WaitForHealthy_WithUnknownHealthStatus_KeepsPolling()
    {
      MockPack
          .SetupContainerCreate("container-123")
          .SetupContainerStart()
          .SetupContainerStop()
          .SetupContainerRemove();
      var unknown = new Container
      {
        Id = "container-123",
        State = new ContainerState
        {
          Running = true,
          Status = "running",
          Health = new Health { Status = HealthState.Unknown }
        }
      };
      var healthy = new Container
      {
        Id = "container-123",
        State = new ContainerState
        {
          Running = true,
          Status = "running",
          Health = new Health { Status = HealthState.Healthy }
        }
      };
      var sequence = MockPack.ContainerDriver
          .SetupSequence(d => d.InspectAsync(
              It.IsAny<DriverContext>(),
              "container-123",
              It.IsAny<CancellationToken>()));
      for (var i = 0; i < 20; i++)
        sequence = sequence.ReturnsAsync(CommandResponse<Container>.Ok(unknown));
      sequence.ReturnsAsync(CommandResponse<Container>.Ok(healthy));

      await using var results = await new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c
              .UseImage("alpine")
              .WithWaitPollInterval(1)
              .WaitForHealthy(100))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);

      Assert.Single(results.All);
    }

    [Fact]
    public async Task CrashedContainerStart_FailsWithExitCodeAndLogs()
    {
      MockPack
          .SetupContainerCreate("container-123")
          .SetupContainerStart()
          .SetupContainerRemove()
          .SetupContainerGetLogs("segfault");
      MockPack.ContainerDriver
          .Setup(d => d.InspectAsync(
              It.IsAny<DriverContext>(),
              "container-123",
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Container>.Ok(new Container
          {
            Id = "container-123",
            State = new ContainerState
            {
              Running = false,
              Status = "exited",
              ExitCode = 137
            }
          }));

      var ex = await Assert.ThrowsAsync<FluentDockerException>(() => new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c.UseImage("alpine"))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken));

      Assert.Contains("exit code 137", ex.Message);
      Assert.Contains("segfault", ex.Message);
    }

    [Fact]
    public async Task UseContainer_WithHostIpPortMapping_PassesValidatedValue()
    {
      MockPack
          .SetupContainerCreate()
          .SetupContainerStart()
          .SetupContainerInspect(running: true)
          .SetupContainerStop()
          .SetupContainerRemove();

      await using var results = await new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c
              .UseImage("nginx")
              .WithPort("127.0.0.1:8080", "80/tcp"))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);

      MockPack.ContainerDriver.Verify(d => d.CreateAsync(
          It.IsAny<DriverContext>(),
          It.Is<ContainerCreateConfig>(cfg =>
              cfg.PortBindings["80/tcp"] == "127.0.0.1:8080"),
          It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UseContainer_WithInvalidHostPort_FailsValidation()
    {
      var ex = await Assert.ThrowsAsync<FluentDockerException>(() => new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c
              .UseImage("nginx")
              .WithPort("not-a-port", "80/tcp"))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken));

      Assert.Contains("Invalid host port", ex.Message);
    }

    [Fact]
    public async Task UseContainer_WithHealthDnsAndStopSignal_PassesConfig()
    {
      MockPack
          .SetupContainerCreate()
          .SetupContainerStart()
          .SetupContainerInspect(running: true)
          .SetupContainerStop()
          .SetupContainerRemove();

      await using var results = await new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c
              .UseImage("nginx")
              .WithHealthCheck("curl -f http://localhost/ || exit 1", "5s", "1s", 3, "10s")
              .WithDns("1.1.1.1", "8.8.8.8")
              .WithStopSignal("SIGTERM"))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);

      MockPack.ContainerDriver.Verify(d => d.CreateAsync(
          It.IsAny<DriverContext>(),
          It.Is<ContainerCreateConfig>(cfg =>
              cfg.HealthCheck != null &&
              cfg.HealthCheck.Test[0] == "CMD-SHELL" &&
              cfg.HealthCheck.Interval == "5s" &&
              cfg.HealthCheck.Timeout == "1s" &&
              cfg.HealthCheck.Retries == 3 &&
              cfg.HealthCheck.StartPeriod == "10s" &&
              cfg.Dns.Count == 2 &&
              cfg.StopSignal == "SIGTERM"),
          It.IsAny<CancellationToken>()), Times.Once);
    }
  }
}
