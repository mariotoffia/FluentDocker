using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Model.Containers;
using FluentDocker.Services;
using FluentDocker.Services.Extensions;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Service
{
  /// <summary>
  /// S-H1: <see cref="ServiceExtensions.WaitForPortAsync(IContainerService, string, long, int, CancellationToken)"/>,
  /// <c>WaitForHttpAsync</c>, and <c>WaitForLogMessageAsync</c> fail fast with a
  /// <see cref="FluentDockerException"/> (exit code + log tail) on a dead container instead of
  /// burning the full wait timeout. S-M4: the shared retriable-exception classifier is a
  /// whitelist of transient failures, not a blacklist - a non-transient exception surfaces
  /// instead of being silently retried.
  /// </summary>
  public partial class ServiceExtensionsTests
  {
    private const long FailFastBudgetMs = 5000;

    private static Container TerminalContainer(int exitCode) => new()
    {
      Id = "dead-container",
      State = new ContainerState { Status = "exited", Running = false, ExitCode = exitCode }
    };

    private static Container RunningContainer() => new()
    {
      Id = "healthy-container",
      State = new ContainerState { Status = "running", Running = true }
    };

    #region S-H1: WaitForPortAsync

    [Fact]
    public async Task WaitForPortAsync_TerminalContainerState_ThrowsFluentDockerExceptionQuickly()
    {
      var mock = new Mock<IContainerService>();
      mock.Setup(s => s.Id).Returns("dead-container");
      mock.Setup(s => s.InspectAsync(It.IsAny<CancellationToken>()))
          .ReturnsAsync(TerminalContainer(exitCode: 1));
      mock.Setup(s => s.GetLogsAsync(false, It.IsAny<CancellationToken>()))
          .ReturnsAsync("fatal: bad config, aborting startup");

      var sw = Stopwatch.StartNew();
      var ex = await Assert.ThrowsAsync<FluentDockerException>(() =>
          mock.Object.WaitForPortAsync(
              "5432/tcp", timeout: 30000, pollIntervalMs: 50,
              cancellationToken: TestContext.Current.CancellationToken));
      sw.Stop();

      Assert.Contains("1", ex.Message);
      Assert.Contains("fatal: bad config, aborting startup", ex.Message);
      Assert.True(sw.ElapsedMilliseconds < FailFastBudgetMs,
          $"Expected fail-fast, took {sw.ElapsedMilliseconds}ms against a 30000ms timeout");
    }

    [Fact]
    public async Task WaitForPortAsync_HealthyContainer_StillReturnsTrue()
    {
      var mock = new Mock<IContainerService>();
      mock.Setup(s => s.Id).Returns("healthy-container");
      mock.Setup(s => s.InspectAsync(It.IsAny<CancellationToken>()))
          .ReturnsAsync(RunningContainer());
      using var listener = new TcpListener(IPAddress.Loopback, 0);
      listener.Start();
      var endpoint = (IPEndPoint)listener.LocalEndpoint!;
      mock.Setup(s => s.ToHostExposedEndpointAsync("5432/tcp", It.IsAny<CancellationToken>()))
          .ReturnsAsync(endpoint);

      var result = await mock.Object.WaitForPortAsync(
          "5432/tcp", timeout: 5000, pollIntervalMs: 50,
          cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(result);
    }

    [Fact]
    public async Task WaitForPortAsync_TerminalState_PreemptsProbe_NotSwallowedByRetryWhitelist()
    {
      // Proves the fail-fast diagnostic is not merely "not classified as retriable" but actually
      // wins the race against the probe: the probe is configured to throw a whitelisted/retriable
      // exception (SocketException) yet is never even invoked, because the terminal-state check
      // runs before the probe on every iteration and throws immediately.
      var probeCalls = 0;
      var mock = new Mock<IContainerService>();
      mock.Setup(s => s.Id).Returns("dead-container");
      mock.Setup(s => s.InspectAsync(It.IsAny<CancellationToken>()))
          .ReturnsAsync(TerminalContainer(exitCode: 137));
      mock.Setup(s => s.GetLogsAsync(false, It.IsAny<CancellationToken>()))
          .ReturnsAsync("oom-killed");
      mock.Setup(s => s.ToHostExposedEndpointAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
          .Callback(() => probeCalls++)
          .ThrowsAsync(new SocketException((int)SocketError.ConnectionReset));

      var ex = await Assert.ThrowsAsync<FluentDockerException>(() =>
          mock.Object.WaitForPortAsync(
              "5432/tcp", timeout: 30000, pollIntervalMs: 50,
              cancellationToken: TestContext.Current.CancellationToken));

      Assert.Contains("137", ex.Message);
      Assert.Contains("oom-killed", ex.Message);
      Assert.Equal(0, probeCalls);
    }

    #endregion

    #region S-H1: WaitForHttpAsync

    [Fact]
    public async Task WaitForHttpAsync_TerminalContainerState_ThrowsFluentDockerExceptionQuickly()
    {
      var mock = new Mock<IContainerService>();
      mock.Setup(s => s.Id).Returns("dead-container");
      mock.Setup(s => s.InspectAsync(It.IsAny<CancellationToken>()))
          .ReturnsAsync(TerminalContainer(exitCode: 1));
      mock.Setup(s => s.GetLogsAsync(false, It.IsAny<CancellationToken>()))
          .ReturnsAsync("fatal: bad config, aborting startup");

      var sw = Stopwatch.StartNew();
      var ex = await Assert.ThrowsAsync<FluentDockerException>(() =>
          mock.Object.WaitForHttpAsync(
              "8080/tcp", "/health", timeout: 30000, pollIntervalMs: 50,
              cancellationToken: TestContext.Current.CancellationToken));
      sw.Stop();

      Assert.Contains("1", ex.Message);
      Assert.Contains("fatal: bad config, aborting startup", ex.Message);
      Assert.True(sw.ElapsedMilliseconds < FailFastBudgetMs,
          $"Expected fail-fast, took {sw.ElapsedMilliseconds}ms against a 30000ms timeout");
    }

    [Fact]
    public async Task WaitForHttpAsync_HealthyContainer_StillReturnsTrue()
    {
      using var listener = new TcpListener(IPAddress.Loopback, 0);
      listener.Start();
      var port = ((IPEndPoint)listener.LocalEndpoint!).Port;
      var server = Task.Run(async () =>
      {
        using var client = await listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        await reader.ReadLineAsync(TestContext.Current.CancellationToken);
        var bytes = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n");
        await stream.WriteAsync(bytes, TestContext.Current.CancellationToken);
      }, TestContext.Current.CancellationToken);
      var mock = new Mock<IContainerService>();
      mock.Setup(s => s.Id).Returns("healthy-container");
      mock.Setup(s => s.InspectAsync(It.IsAny<CancellationToken>()))
          .ReturnsAsync(RunningContainer());
      mock.Setup(s => s.ToHostExposedEndpointAsync("80/tcp", It.IsAny<CancellationToken>()))
          .ReturnsAsync(new IPEndPoint(IPAddress.Loopback, port));

      var result = await mock.Object.WaitForHttpAsync(
          "80/tcp", "/health", timeout: 5000, pollIntervalMs: 50,
          cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(result);
      await server;
    }

    #endregion

    #region S-H1: WaitForLogMessageAsync

    [Fact]
    public async Task WaitForLogMessageAsync_TerminalContainerState_ThrowsFluentDockerExceptionQuickly()
    {
      var mock = new Mock<IContainerService>();
      mock.Setup(s => s.Id).Returns("dead-container");
      mock.Setup(s => s.InspectAsync(It.IsAny<CancellationToken>()))
          .ReturnsAsync(TerminalContainer(exitCode: 1));
      mock.Setup(s => s.GetLogsAsync(false, It.IsAny<CancellationToken>()))
          .ReturnsAsync("fatal: bad config, aborting startup");

      var sw = Stopwatch.StartNew();
      var ex = await Assert.ThrowsAsync<FluentDockerException>(() =>
          mock.Object.WaitForLogMessageAsync(
              "ready to accept connections", timeout: 30000, pollIntervalMs: 50,
              cancellationToken: TestContext.Current.CancellationToken));
      sw.Stop();

      Assert.Contains("1", ex.Message);
      Assert.Contains("fatal: bad config, aborting startup", ex.Message);
      Assert.True(sw.ElapsedMilliseconds < FailFastBudgetMs,
          $"Expected fail-fast, took {sw.ElapsedMilliseconds}ms against a 30000ms timeout");
    }

    [Fact]
    public async Task WaitForLogMessageAsync_HealthyContainer_StillReturnsTrue()
    {
      var mock = new Mock<IContainerService>();
      mock.Setup(s => s.Id).Returns("healthy-container");
      mock.Setup(s => s.InspectAsync(It.IsAny<CancellationToken>()))
          .ReturnsAsync(RunningContainer());
      mock.Setup(s => s.GetLogsAsync(false, It.IsAny<CancellationToken>()))
          .ReturnsAsync("booting...\nready to accept connections");

      var result = await mock.Object.WaitForLogMessageAsync(
          "ready to accept connections", timeout: 5000, pollIntervalMs: 50,
          cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(result);
    }

    #endregion

    #region S-M4: IsRetriableWaitException whitelist

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WaitForLogMessageAsync_TransientProbeException_IsRetried_ThenSucceeds(
        bool useHttpRequestException)
    {
      var calls = 0;
      var mock = new Mock<IContainerService>();
      mock.Setup(s => s.Id).Returns("c1");
      mock.Setup(s => s.GetLogsAsync(false, It.IsAny<CancellationToken>()))
          .Returns(() =>
          {
            calls++;
            if (calls == 1)
            {
              Exception ex = useHttpRequestException
                  ? new HttpRequestException("connection reset")
                  : new SocketException((int)SocketError.ConnectionReset);
              return Task.FromException<string>(ex);
            }

            return Task.FromResult("ready");
          });

      var result = await mock.Object.WaitForLogMessageAsync(
          "ready", timeout: 5000, pollIntervalMs: 10,
          cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(result);
      Assert.True(calls >= 2);
    }

    [Fact]
    public async Task WaitForLogMessageAsync_NonTransientInvalidOperationException_SurfacesInsteadOfRetrying()
    {
      var calls = 0;
      var mock = new Mock<IContainerService>();
      mock.Setup(s => s.Id).Returns("c1");
      mock.Setup(s => s.GetLogsAsync(false, It.IsAny<CancellationToken>()))
          .Returns(() =>
          {
            calls++;
            return Task.FromException<string>(new InvalidOperationException("deterministic bug"));
          });

      await Assert.ThrowsAsync<InvalidOperationException>(() =>
          mock.Object.WaitForLogMessageAsync(
              "ready", timeout: 5000, pollIntervalMs: 10,
              cancellationToken: TestContext.Current.CancellationToken));

      Assert.Equal(1, calls);
    }

    #endregion
  }
}
