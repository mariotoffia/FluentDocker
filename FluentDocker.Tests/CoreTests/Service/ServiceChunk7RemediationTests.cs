using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Podman;
using FluentDocker.Kernel;
using FluentDocker.Model.Containers;
using FluentDocker.Model.Drivers;
using FluentDocker.Services;
using FluentDocker.Services.Extensions;
using FluentDocker.Services.Impl;
using FluentDocker.Tests.Mocks;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Service
{
  [Trait("Category", "Unit")]
  public class ServiceChunk7RemediationTests : MockKernelTestBase, IAsyncLifetime
  {
    public async ValueTask InitializeAsync()
    {
      await InitializeMockKernelAsync();
    }

    [Fact]
    public async Task StopAsync_StateChangeHandlerCanReenterLifecycleWithoutDeadlocking()
    {
      MockPack.SetupContainerStart();
      MockPack.ContainerDriver
          .Setup(d => d.StopAsync(It.IsAny<DriverContext>(), "container-123", It.IsAny<int?>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));
      // Force the RemoveAsync continuation onto a pool thread so the reentrant UpdateState(Removed)
      // runs on a DIFFERENT thread than the one holding _stateLock. That cross-thread hop is what
      // turns the under-lock handler invocation (the 7.1 bug) into a real deadlock; a synchronous
      // mock keeps everything on one thread where Monitor reentrancy hides the bug.
      MockPack.ContainerDriver
          .Setup(d => d.RemoveAsync(
              It.IsAny<DriverContext>(), "container-123", It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
          .Returns(async () =>
          {
            await Task.Delay(50, TestContext.Current.CancellationToken);
            return CommandResponse<Unit>.Ok(Unit.Default);
          });
      var service = new ContainerService(Kernel, DriverId, "container-123", "alpine", "test");
      await service.StartAsync(TestContext.Current.CancellationToken);
      var reentered = false;
      service.StateChange += (_, args) =>
      {
        if (args.State != ServiceRunningState.Stopped || reentered)
          return;
        reentered = true;
        service.RemoveAsync(force: true, TestContext.Current.CancellationToken).GetAwaiter().GetResult();
      };

      var stopTask = service.StopAsync(TestContext.Current.CancellationToken);
      var completed = await Task.WhenAny(stopTask, Task.Delay(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));

      Assert.Same(stopTask, completed);
      await stopTask;
      Assert.True(reentered);
      Assert.Equal(ServiceRunningState.Removed, service.State);
    }

    [Fact]
    public async Task StopAsync_WhenAlreadyStopped_DoesNotFirePhantomTransitions()
    {
      MockPack.SetupContainerStart();
      MockPack.ContainerDriver
          .Setup(d => d.StopAsync(It.IsAny<DriverContext>(), "container-123", It.IsAny<int?>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));
      var service = new ContainerService(Kernel, DriverId, "container-123", "alpine", "test");
      await service.StartAsync(TestContext.Current.CancellationToken);
      await service.StopAsync(TestContext.Current.CancellationToken);
      var states = new List<ServiceRunningState>();
      service.StateChange += (_, args) => states.Add(args.State);

      await service.StopAsync(TestContext.Current.CancellationToken);

      Assert.Empty(states);
      MockPack.ContainerDriver.Verify(d => d.StopAsync(
          It.IsAny<DriverContext>(), "container-123", It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CopyFromAsync_WhenTempCleanupCannotDelete_DoesNotMaskSuccessfulCopy()
    {
      var tempRoot = Path.Combine(Path.GetTempPath(), "fluentdocker-copyfrom");
      Directory.CreateDirectory(tempRoot);
      MockPack.ContainerDriver
          .Setup(d => d.CopyFromAsync(
              It.IsAny<DriverContext>(), "container-123", "/data/file", It.IsAny<string>(), It.IsAny<CancellationToken>()))
          .Callback<DriverContext, string, string, string, CancellationToken>((_, _, _, hostPath, _) =>
          {
            File.WriteAllText(hostPath, "ok");
            if (!OperatingSystem.IsWindows())
              File.SetUnixFileMode(tempRoot, UnixFileMode.UserRead | UnixFileMode.UserExecute);
          })
          .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));
      var service = new ContainerService(Kernel, DriverId, "container-123", "alpine", "test");

      try
      {
        var bytes = await service.CopyFromAsync("/data/file", TestContext.Current.CancellationToken);

        Assert.Equal("ok", System.Text.Encoding.UTF8.GetString(bytes));
      }
      finally
      {
        if (!OperatingSystem.IsWindows() && Directory.Exists(tempRoot))
          File.SetUnixFileMode(tempRoot, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        if (Directory.Exists(tempRoot))
          Directory.Delete(tempRoot, recursive: true);
      }
    }

    [Fact]
    public async Task CopyToLifecycleHook_WhenHostPathMissing_LogsWarningAndSkipsDriver()
    {
      var loggerFactory = new CapturingLoggerFactory();
      var pack = new MockDriverPack();
      var context = new DriverContext(DriverId);
      await pack.InitializeAsync(context, TestContext.Current.CancellationToken);
      var kernel = new FluentDockerKernel(new DriverRegistry(loggerFactory), loggerFactory);
      await kernel.RegisterDriverPackAsync(DriverId, pack, context, TestContext.Current.CancellationToken);
      var missing = Path.Combine(".out", "missing-copyto-source.txt");
      var service = new ContainerService(
          kernel,
          DriverId,
          "container-123",
          "alpine",
          "test",
          stopOnDispose: false,
          deleteOnDispose: false,
          lifecycleHooks:
          [
            new LifecycleHook
            {
              Type = LifecycleHookType.CopyTo,
              TriggerState = ServiceRunningState.Removing,
              HostPath = missing,
              ContainerPath = "/data/file"
            }
          ]);

      await service.DisposeAsync();
      await kernel.DisposeAsync();

      Assert.Contains(loggerFactory.Entries, e =>
          e.Level == LogLevel.Warning &&
          e.Message.Contains(missing, StringComparison.Ordinal) &&
          e.Message.Contains("container-123", StringComparison.Ordinal));
      pack.ContainerDriver.Verify(d => d.CopyToAsync(
          It.IsAny<DriverContext>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
          Times.Never);
    }

    [Fact]
    public async Task ComposeRefreshStateAsync_AfterRemove_DoesNotResurrectRemovedProject()
    {
      MockPack.SetupComposeDown().SetupComposeList();
      var service = new ComposeService(Kernel, DriverId, [], "project");
      var removedCount = 0;
      service.StateChange += (_, args) =>
      {
        if (args.State == ServiceRunningState.Removed)
          removedCount++;
      };
      await service.RemoveAsync(cancellationToken: TestContext.Current.CancellationToken);

      await service.RefreshStateAsync(TestContext.Current.CancellationToken);

      Assert.Equal(ServiceRunningState.Removed, service.State);
      Assert.Equal(1, removedCount);
    }

    [Fact]
    public async Task Operations_AfterDispose_ThrowObjectDisposedException()
    {
      var service = new ContainerService(
          Kernel, DriverId, "container-123", "alpine", "test", stopOnDispose: false, deleteOnDispose: false);
      await service.DisposeAsync();

      await Assert.ThrowsAsync<ObjectDisposedException>(() => service.StopAsync(TestContext.Current.CancellationToken));
      await Assert.ThrowsAsync<ObjectDisposedException>(() => service.ExecuteAsync("echo hi", TestContext.Current.CancellationToken));
    }


    [Fact]
    public async Task RemoveAsync_AfterDispose_ThrowsObjectDisposedException()
    {
      var service = new ContainerService(
          Kernel, DriverId, "container-123", "alpine", "test", stopOnDispose: false, deleteOnDispose: false);
      await service.DisposeAsync();

      await Assert.ThrowsAsync<ObjectDisposedException>(() =>
          service.RemoveAsync(force: true, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DisposeAsync_CopyFromLifecycleHookStillRunsAfterDisposedFlagSet()
    {
      var hostPath = Path.Combine(".out", "chunk7-copyfrom-hook.txt");
      if (File.Exists(hostPath))
        File.Delete(hostPath);
      MockPack.ContainerDriver
          .Setup(d => d.CopyFromAsync(
              It.IsAny<DriverContext>(), "container-123", "/data/file", hostPath, It.IsAny<CancellationToken>()))
          .Callback(() => File.WriteAllText(hostPath, "copied"))
          .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));
      var service = new ContainerService(
          Kernel,
          DriverId,
          "container-123",
          "alpine",
          "test",
          stopOnDispose: false,
          deleteOnDispose: false,
          lifecycleHooks:
          [
            new LifecycleHook
            {
              Type = LifecycleHookType.CopyFrom,
              TriggerState = ServiceRunningState.Removing,
              HostPath = hostPath,
              ContainerPath = "/data/file"
            }
          ]);

      await service.DisposeAsync();

      Assert.True(File.Exists(hostPath));
      Assert.Equal("copied", File.ReadAllText(hostPath));
      File.Delete(hostPath);
    }

    [Fact]
    public async Task Dispose_UserHookDuringDispose_CanCallReadOpWithoutObjectDisposedException()
    {
      MockPack.SetupContainerStart().SetupContainerStop().SetupContainerRemove().SetupContainerGetLogs();
      var service = new ContainerService(Kernel, DriverId, "container-123", "alpine", "test");
      await service.StartAsync(TestContext.Current.CancellationToken);
      Exception captured = null;
      var hookRan = false;
      // A teardown hook that reads container state during dispose (e.g. capturing logs before removal)
      // must observe the still-live container, not ObjectDisposedException (M1). The disposed guard
      // keys on dispose COMPLETION, so this read succeeds while dispose is in flight.
      service.AddHook(
          ServiceRunningState.Stopping,
          async _ =>
          {
            try
            {
              await service.GetLogsAsync(cancellationToken: TestContext.Current.CancellationToken);
              hookRan = true;
            }
            catch (Exception ex)
            {
              captured = ex;
            }
          },
          "read-during-dispose");

      await service.DisposeAsync();

      Assert.Null(captured);
      Assert.True(hookRan);
    }

    [Fact]
    public async Task PauseAsync_WhenContainerRemoved_ReturnsWithoutDriverCall()
    {
      var service = new ContainerService(
          Kernel, DriverId, "container-123", "alpine", "test", initialState: ServiceRunningState.Removed);

      await service.PauseAsync(TestContext.Current.CancellationToken);

      MockPack.ContainerDriver.Verify(d => d.PauseAsync(
          It.IsAny<DriverContext>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PauseAsync_WhenDriverThrows_NormalizesStateToUnknown()
    {
      MockPack.SetupContainerStart();
      MockPack.ContainerDriver
          .Setup(d => d.PauseAsync(It.IsAny<DriverContext>(), "container-123", It.IsAny<CancellationToken>()))
          .ThrowsAsync(new InvalidOperationException("daemon gone"));
      var service = new ContainerService(Kernel, DriverId, "container-123", "alpine", "test");
      await service.StartAsync(TestContext.Current.CancellationToken);

      await Assert.ThrowsAsync<InvalidOperationException>(() => service.PauseAsync(TestContext.Current.CancellationToken));

      Assert.Equal(ServiceRunningState.Unknown, service.State);
    }

    [Fact]
    public async Task PodStopAsync_WhenAlreadyStoppedError_SucceedsAndKeepsStopped()
    {
      var podDriver = new Mock<IPodmanPodDriver>();
      MockPack.RegisterCustomDriver(podDriver.Object);
      podDriver
          .Setup(d => d.StopPodAsync(It.IsAny<DriverContext>(), "pod", It.IsAny<int?>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Fail("pod pod is not running", ErrorCodes.Pod.StopFailed));
      var service = new PodService(Kernel, DriverId, "pod-id", "pod");

      await service.StopAsync(TestContext.Current.CancellationToken);

      Assert.Equal(ServiceRunningState.Stopped, service.State);
    }

    [Fact]
    public async Task PodStopAsync_WhenRemoved_IsNoOpAndDoesNotResurrect()
    {
      var podDriver = new Mock<IPodmanPodDriver>();
      MockPack.RegisterCustomDriver(podDriver.Object);
      podDriver
          .Setup(d => d.RemovePodAsync(It.IsAny<DriverContext>(), "pod", It.IsAny<bool>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));
      var service = new PodService(Kernel, DriverId, "pod-id", "pod");
      await service.RemoveAsync(force: true, TestContext.Current.CancellationToken);
      var states = new List<ServiceRunningState>();
      service.StateChange += (_, args) => states.Add(args.State);

      await service.StopAsync(TestContext.Current.CancellationToken);

      Assert.Equal(ServiceRunningState.Removed, service.State);
      Assert.Empty(states);
      podDriver.Verify(
          d => d.StopPodAsync(It.IsAny<DriverContext>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()),
          Times.Never);
    }

    [Fact]
    public async Task ExecuteDetailedAsync_ReturnsExitCodeStdoutAndStderr()
    {
      MockPack.ContainerDriver
          .Setup(d => d.ExecAsync(It.IsAny<DriverContext>(), "container-123", It.IsAny<ExecConfig>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<ExecResult>.Ok(new ExecResult { ExitCode = 3, StdOut = "out", StdErr = "err" }));
      var service = new ContainerService(Kernel, DriverId, "container-123", "alpine", "test");

      var result = await service.ExecuteDetailedAsync(["sh", "-c", "exit 3"], TestContext.Current.CancellationToken);

      Assert.Equal(3, result.ExitCode);
      Assert.Equal("out", result.StdOut);
      Assert.Equal("err", result.StdErr);
    }

    [Fact]
    public async Task Constructor_CopiesLifecycleHooksSoLaterListMutationDoesNotAffectService()
    {
      var hooks = new List<LifecycleHook>();
      var service = new ContainerService(
          Kernel,
          DriverId,
          "container-123",
          "alpine",
          "test",
          stopOnDispose: false,
          deleteOnDispose: false,
          lifecycleHooks: hooks);
      var hook = new LifecycleHook
      {
        Type = LifecycleHookType.CopyFrom,
        TriggerState = ServiceRunningState.Removing,
        HostPath = Path.Combine(".out", "should-not-copy.txt"),
        ContainerPath = "/data/file",
        Command = ["before"]
      };
      hooks.Add(hook);
      hook.HostPath = Path.Combine(".out", "mutated-should-not-copy.txt");
      hook.Command[0] = "after";

      await service.DisposeAsync();

      MockPack.ContainerDriver.Verify(d => d.CopyFromAsync(
          It.IsAny<DriverContext>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
          Times.Never);
    }

    [Fact]
    public async Task GetDockerHostAddressAsync_WithPreCanceledToken_ThrowsPromptly()
    {
      using var cts = new CancellationTokenSource();
      await cts.CancelAsync();

      await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
          EnvironmentExtensions.GetDockerHostAddressAsync(useCache: false, cts.Token));
    }

    [Fact]
    public async Task ToHostExposedEndpointAsync_SkipsMalformedBindingsAndReturnsFirstParseable()
    {
      MockPack.ContainerDriver
          .Setup(d => d.InspectAsync(It.IsAny<DriverContext>(), "container-123", It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Container>.Ok(new Container
          {
            Id = "container-123",
            NetworkSettings = new ContainerNetworkSettings
            {
              Ports = new Dictionary<string, HostIpEndpoint[]>
              {
                ["80/tcp"] =
                [
                  new HostIpEndpoint { HostIp = "127.0.0.1", HostPort = "not-a-port" },
                  new HostIpEndpoint { HostIp = "127.0.0.1", HostPort = "5555" }
                ]
              }
            }
          }));
      var service = new ContainerService(Kernel, DriverId, "container-123", "alpine", "test");

      var endpoint = await service.ToHostExposedEndpointAsync("80/tcp", TestContext.Current.CancellationToken);

      Assert.Equal(IPAddress.Loopback, endpoint.Address);
      Assert.Equal(5555, endpoint.Port);
    }

    private sealed class CapturingLoggerFactory : ILoggerFactory
    {
      private readonly ConcurrentBag<LogEntry> _entries = [];

      public IReadOnlyCollection<LogEntry> Entries => _entries;

      public void AddProvider(ILoggerProvider provider)
      {
      }

      public ILogger CreateLogger(string categoryName) => new CapturingLogger(_entries);

      public void Dispose()
      {
      }
    }

    private sealed class CapturingLogger : ILogger
    {
      private readonly ConcurrentBag<LogEntry> _entries;

      public CapturingLogger(ConcurrentBag<LogEntry> entries) => _entries = entries;

      public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

      public bool IsEnabled(LogLevel logLevel) => true;

      public void Log<TState>(
          LogLevel logLevel,
          EventId eventId,
          TState state,
          Exception? exception,
          Func<TState, Exception?, string> formatter)
      {
        _entries.Add(new LogEntry(logLevel, formatter(state, exception)));
      }
    }

    private sealed class NullScope : IDisposable
    {
      public static readonly NullScope Instance = new();

      public void Dispose()
      {
      }
    }

    private sealed record LogEntry(LogLevel Level, string Message);
  }
}
