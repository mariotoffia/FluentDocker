using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
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
  public class ServicesLayerProductionReadinessTests : MockKernelTestBase, IAsyncLifetime
  {
    public async ValueTask InitializeAsync()
    {
      await InitializeMockKernelAsync();
    }

    [Fact]
    public async Task ContainerRemoveAsync_WhenDriverThrows_MarksUnknown()
    {
      MockPack.ContainerDriver
          .Setup(d => d.RemoveAsync(
              It.IsAny<DriverContext>(), "container-123", It.IsAny<bool>(), It.IsAny<bool>(),
              It.IsAny<CancellationToken>()))
          .ThrowsAsync(new InvalidOperationException("daemon disconnected"));
      var service = new ContainerService(Kernel, DriverId, "container-123", "alpine", "test");

      await Assert.ThrowsAsync<InvalidOperationException>(() =>
          service.RemoveAsync(cancellationToken: TestContext.Current.CancellationToken));

      Assert.Equal(ServiceRunningState.Unknown, service.State);
    }

    [Fact]
    public async Task NetworkRemoveAsync_WhenDriverThrows_MarksUnknown()
    {
      MockPack.NetworkDriver
          .Setup(d => d.RemoveAsync(
              It.IsAny<DriverContext>(), "net-1", It.IsAny<CancellationToken>()))
          .ThrowsAsync(new InvalidOperationException("daemon disconnected"));
      var service = new NetworkService(Kernel, DriverId, "net-1", "net");

      await Assert.ThrowsAsync<InvalidOperationException>(() =>
          service.RemoveAsync(cancellationToken: TestContext.Current.CancellationToken));

      Assert.Equal(ServiceRunningState.Unknown, service.State);
    }

    [Fact]
    public async Task DisposeAsync_KeptRunningContainer_DoesNotEmitRemovingOrChangeState()
    {
      var service = new ContainerService(
          Kernel,
          DriverId,
          "container-123",
          "alpine",
          "test",
          stopOnDispose: false,
          deleteOnDispose: false,
          initialState: ServiceRunningState.Running);
      var states = new List<ServiceRunningState>();
      service.StateChange += (_, args) => states.Add(args.State);

      await service.DisposeAsync();

      Assert.DoesNotContain(ServiceRunningState.Removing, states);
      Assert.Equal(ServiceRunningState.Running, service.State);
      MockPack.ContainerDriver.Verify(d => d.StopAsync(
          It.IsAny<DriverContext>(), It.IsAny<string>(), It.IsAny<int?>(),
          It.IsAny<CancellationToken>()), Times.Never);
      MockPack.ContainerDriver.Verify(d => d.RemoveAsync(
          It.IsAny<DriverContext>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(),
          It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DisposeAsync_DeleteOnDispose_StillTransitionsToRemoved()
    {
      MockPack.SetupContainerRemove();
      var service = new ContainerService(
          Kernel,
          DriverId,
          "container-123",
          "alpine",
          "test",
          stopOnDispose: false,
          deleteOnDispose: true,
          initialState: ServiceRunningState.Running);
      var states = new List<ServiceRunningState>();
      service.StateChange += (_, args) => states.Add(args.State);

      await service.DisposeAsync();

      Assert.Contains(ServiceRunningState.Removing, states);
      Assert.Contains(ServiceRunningState.Removed, states);
      Assert.Equal(ServiceRunningState.Removed, service.State);
    }

    [Fact]
    public async Task EngineScopeDisposeAsync_WhenRestoreReturnsFalse_LogsRestoreFailure()
    {
      var loggerFactory = new CapturingLoggerFactory();
      var mockPack = new MockDriverPack();
      mockPack.SetupSystemIsWindowsEngine(false);
      mockPack.SetupSystemSwitchToWindows();
      mockPack.SetupSystemSwitchToLinux();
      var context = new DriverContext("docker");
      await mockPack.InitializeAsync(context);
      await using var kernel = new FluentDockerKernel(new DriverRegistry(loggerFactory), loggerFactory);
      await kernel.RegisterDriverPackAsync("docker", mockPack, context);
      kernel.SetDefaultDriver("docker");
      var scope = await EngineScope.CreateAsync(
          kernel, "docker", EngineScopeType.Windows, TestContext.Current.CancellationToken);
      mockPack.SystemDriver
          .Setup(d => d.SwitchToLinuxDaemonAsync(
              It.IsAny<DriverContext>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Fail("restore failed"));

      await scope.DisposeAsync();

      Assert.Contains(loggerFactory.Entries, entry =>
          entry.Level == LogLevel.Error &&
          entry.Message.Contains("restore failed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task WaitForLogMessageAsync_WhenMessageScrolledPastTail_ChecksFullLogsBeforeCancellation()
    {
      var fullPolls = 0;
      var tails = new List<int?>();
      MockPack.ContainerDriver
          .Setup(d => d.GetLogsAsync(
              It.IsAny<DriverContext>(), "container-123", false, It.IsAny<int?>(), false,
              It.IsAny<CancellationToken>()))
          .Callback<DriverContext, string, bool, int?, bool, CancellationToken>((_, _, _, tail, _, _) =>
              tails.Add(tail))
          .ReturnsAsync(() =>
          {
            if (tails[^1] != null)
              return CommandResponse<string>.Ok("newer noise");

            fullPolls++;
            return CommandResponse<string>.Ok(fullPolls == 1 ? "booting" : "booting\nready");
          });
      var service = new ContainerService(Kernel, DriverId, "container-123", "alpine", "test");
      using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
      cts.CancelAfter(TimeSpan.FromSeconds(5));

      var found = await service.WaitForLogMessageAsync(
          "ready",
          timeout: 2_000,
          pollIntervalMs: 10,
          cancellationToken: cts.Token);

      Assert.True(found);
      Assert.True(fullPolls >= 2);
      Assert.Contains(tails, tail => tail == 100);
    }

    [Fact]
    public async Task WaitForLogMessageAsync_NonContainerService_DoesFinalFullLogScan()
    {
      var calls = 0;
      var service = new Mock<IContainerService>();
      service.Setup(s => s.GetLogsAsync(false, It.IsAny<CancellationToken>()))
          .Returns(() =>
          {
            calls++;
            return calls == 1
                ? Task.FromException<string>(new DriverException(
                    "temporary", ErrorCodes.General.Timeout, isTransient: true))
                : Task.FromResult("ready");
          });

      var found = await service.Object.WaitForLogMessageAsync(
          "ready",
          timeout: 1,
          pollIntervalMs: 10,
          cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(found);
      Assert.Equal(2, calls);
    }

    [Fact]
    public async Task ComposePauseAsync_WhenDriverThrows_MarksUnknown()
    {
      MockPack.ComposeDriver
          .Setup(d => d.PauseAsync(
              It.IsAny<DriverContext>(), It.IsAny<ComposeFileConfig>(), It.IsAny<CancellationToken>()))
          .ThrowsAsync(new InvalidOperationException("daemon disconnected"));
      var service = new ComposeService(Kernel, DriverId, [], "project");

      await Assert.ThrowsAsync<InvalidOperationException>(() =>
          service.PauseAsync(TestContext.Current.CancellationToken));

      Assert.Equal(ServiceRunningState.Unknown, service.State);
    }

    [Fact]
    public async Task ComposeUnpauseAsync_WhenDriverThrows_MarksUnknown()
    {
      MockPack.ComposeDriver
          .Setup(d => d.UnpauseAsync(
              It.IsAny<DriverContext>(), It.IsAny<ComposeFileConfig>(), It.IsAny<CancellationToken>()))
          .ThrowsAsync(new InvalidOperationException("daemon disconnected"));
      var service = new ComposeService(Kernel, DriverId, [], "project");

      await Assert.ThrowsAsync<InvalidOperationException>(() =>
          service.UnpauseAsync(TestContext.Current.CancellationToken));

      Assert.Equal(ServiceRunningState.Unknown, service.State);
    }

    [Fact]
    public void ComposeService_DefaultState_IsStopped()
    {
      var service = new ComposeService(Kernel, DriverId, ["compose.yml"], "project");

      Assert.Equal(ServiceRunningState.Stopped, service.State);
    }

    [Fact]
    public void ComposeService_CopiesComposeFiles()
    {
      var files = new List<string> { "compose.yml" };
      var service = new ComposeService(Kernel, DriverId, files, "project");

      files.Add("override.yml");

      Assert.Equal(["compose.yml"], service.ComposeFiles);
    }

    [Fact]
    public async Task ComposeRefreshStateAsync_WhenServicePaused_SetsUnknown()
    {
      MockPack.SetupComposeList(new ComposeServiceInfo { Name = "web", State = "paused" });
      var service = new ComposeService(Kernel, DriverId, ["compose.yml"], "project");

      await service.RefreshStateAsync(TestContext.Current.CancellationToken);

      Assert.Equal(ServiceRunningState.Unknown, service.State);
    }

    [Fact]
    public async Task HostServiceGetSystemInfoAsync_WhenCanceled_DoesNotCallDriver()
    {
      var service = new HostService(Kernel, DriverId, "host");
      using var cts = new CancellationTokenSource();
      await cts.CancelAsync();

      await Assert.ThrowsAsync<OperationCanceledException>(() => service.GetSystemInfoAsync(cts.Token));

      MockPack.SystemDriver.Verify(d => d.GetInfoAsync(
          It.IsAny<DriverContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HostServiceGetSystemInfoAsync_AfterDispose_ThrowsObjectDisposedException()
    {
      var service = new HostService(Kernel, DriverId, "host");
      await service.DisposeAsync();

      await Assert.ThrowsAsync<ObjectDisposedException>(() =>
          service.GetSystemInfoAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void ContainerAddHook_AfterDispose_ThrowsObjectDisposedException()
    {
      var service = new ContainerService(
          Kernel, DriverId, "container-123", "alpine", "test",
          stopOnDispose: false, deleteOnDispose: false);
      service.Dispose();

      Assert.Throws<ObjectDisposedException>(() =>
          service.AddHook(ServiceRunningState.Running, _ => Task.CompletedTask));
      Assert.Throws<ObjectDisposedException>(() => service.RemoveHook("missing"));
    }

    [Fact]
    public async Task ContainerUnpauseAsync_WhenRemoved_ThrowsWithoutDriverCall()
    {
      var service = new ContainerService(
          Kernel, DriverId, "container-123", "alpine", "test",
          initialState: ServiceRunningState.Removed);

      await Assert.ThrowsAsync<InvalidOperationException>(() =>
          service.UnpauseAsync(TestContext.Current.CancellationToken));

      MockPack.ContainerDriver.Verify(d => d.UnpauseAsync(
          It.IsAny<DriverContext>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task WaitForPortAsync_WhenEndpointResolutionThrowsSocketException_RetriesUntilTimeout()
    {
      var calls = 0;
      var service = new Mock<IContainerService>();
      service.Setup(s => s.ToHostExposedEndpointAsync("80/tcp", It.IsAny<CancellationToken>()))
          .Returns(() =>
          {
            calls++;
            return calls == 1
                ? Task.FromException<IPEndPoint>(new SocketException((int)SocketError.HostNotFound))
                : Task.FromResult<IPEndPoint>(null);
          });

      var found = await service.Object.WaitForPortAsync(
          "80/tcp",
          timeout: 1,
          pollIntervalMs: 1,
          cancellationToken: TestContext.Current.CancellationToken);

      Assert.False(found);
      Assert.True(calls >= 1);
    }

    [Fact]
    public async Task WaitForHttpAsync_WhenEndpointResolutionThrowsSocketException_RetriesUntilTimeout()
    {
      var calls = 0;
      var service = new Mock<IContainerService>();
      service.Setup(s => s.ToHostExposedEndpointAsync("80/tcp", It.IsAny<CancellationToken>()))
          .Returns(() =>
          {
            calls++;
            return calls == 1
                ? Task.FromException<IPEndPoint>(new SocketException((int)SocketError.HostNotFound))
                : Task.FromResult<IPEndPoint>(null);
          });

      var found = await service.Object.WaitForHttpAsync(
          "80/tcp",
          "/",
          timeout: 1,
          pollIntervalMs: 1,
          cancellationToken: TestContext.Current.CancellationToken);

      Assert.False(found);
      Assert.True(calls >= 1);
    }

    private sealed class CapturingLoggerFactory : ILoggerFactory
    {
      public ConcurrentBag<LogEntry> Entries { get; } = [];

      public void AddProvider(ILoggerProvider provider)
      {
      }

      public ILogger CreateLogger(string categoryName) => new CapturingLogger(Entries);

      public void Dispose()
      {
      }
    }

    private sealed class CapturingLogger(ConcurrentBag<LogEntry> entries) : ILogger
    {
      public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

      public bool IsEnabled(LogLevel logLevel) => true;

      public void Log<TState>(
          LogLevel logLevel,
          EventId eventId,
          TState state,
          Exception exception,
          Func<TState, Exception, string> formatter)
      {
        var message = formatter(state, exception);
        if (exception != null)
          message = $"{message}: {exception.Message}";
        entries.Add(new LogEntry(logLevel, message));
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
