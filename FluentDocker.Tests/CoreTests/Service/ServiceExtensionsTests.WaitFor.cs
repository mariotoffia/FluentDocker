using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Services;
using FluentDocker.Services.Extensions;
using Moq;
using Xunit;


namespace FluentDocker.Tests.CoreTests.Service
{
  /// <summary>
  /// Unit tests for <see cref="ServiceExtensions"/> wait-for methods:
  /// WaitForLogMessageAsync and WaitForProcessAsync.
  /// </summary>
  public partial class ServiceExtensionsTests
  {
    #region WaitForLogMessageAsync

    [Fact]
    public async Task WaitForLogMessageAsync_LogsContainText_ReturnsTrueImmediately()
    {
      // Arrange
      var mock = new Mock<IContainerService>();
      mock.Setup(s => s.Id).Returns("test-id");
      mock.Setup(s => s.GetLogsAsync(false, It.IsAny<CancellationToken>()))
          .ReturnsAsync("Server started on port 5432\nReady to accept connections");

      // Act
      var result = await mock.Object.WaitForLogMessageAsync(
          "Ready to accept connections", timeout: 5000, cancellationToken: TestContext.Current.CancellationToken);

      // Assert
      Assert.True(result);
      mock.Verify(s => s.GetLogsAsync(false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task WaitForLogMessageAsync_LogsContainPartialMatch_ReturnsTrueImmediately()
    {
      // Arrange
      var mock = new Mock<IContainerService>();
      mock.Setup(s => s.Id).Returns("test-id");
      mock.Setup(s => s.GetLogsAsync(false, It.IsAny<CancellationToken>()))
          .ReturnsAsync("2026-03-27T12:00:00Z INFO database initialized successfully");

      // Act
      var result = await mock.Object.WaitForLogMessageAsync(
          "database initialized", timeout: 5000, cancellationToken: TestContext.Current.CancellationToken);

      // Assert
      Assert.True(result);
    }

    [Fact]
    public async Task WaitForLogMessageAsync_LogsDoNotContainText_TimesOutReturnsFalse()
    {
      // Arrange
      var mock = new Mock<IContainerService>();
      mock.Setup(s => s.Id).Returns("test-id");
      mock.Setup(s => s.GetLogsAsync(false, It.IsAny<CancellationToken>()))
          .ReturnsAsync("Starting application...");

      // Act
      var result = await mock.Object.WaitForLogMessageAsync(
          "Ready to accept connections", timeout: 200, cancellationToken: TestContext.Current.CancellationToken);

      // Assert
      Assert.False(result);
    }

    [Fact]
    public async Task WaitForLogMessageAsync_EmptyLogs_TimesOutReturnsFalse()
    {
      // Arrange
      var mock = new Mock<IContainerService>();
      mock.Setup(s => s.Id).Returns("test-id");
      mock.Setup(s => s.GetLogsAsync(false, It.IsAny<CancellationToken>()))
          .ReturnsAsync(string.Empty);

      // Act
      var result = await mock.Object.WaitForLogMessageAsync("any text", timeout: 200, cancellationToken: TestContext.Current.CancellationToken);

      // Assert
      Assert.False(result);
    }

    [Fact]
    public async Task WaitForLogMessageAsync_NullLogs_TimesOutReturnsFalse()
    {
      // Arrange
      var mock = new Mock<IContainerService>();
      mock.Setup(s => s.Id).Returns("test-id");
      mock.Setup(s => s.GetLogsAsync(false, It.IsAny<CancellationToken>()))
          .ReturnsAsync((string)null);

      // Act
      var result = await mock.Object.WaitForLogMessageAsync("any text", timeout: 200, cancellationToken: TestContext.Current.CancellationToken);

      // Assert
      Assert.False(result);
    }

    [Fact]
    public async Task WaitForLogMessageAsync_TextAppearsOnSecondPoll_ReturnsTrue()
    {
      // Arrange - first call returns no match, second call returns matching log
      var callCount = 0;
      var mock = new Mock<IContainerService>();
      mock.Setup(s => s.Id).Returns("test-id");
      mock.Setup(s => s.GetLogsAsync(false, It.IsAny<CancellationToken>()))
          .ReturnsAsync(() =>
          {
            callCount++;
            return callCount < 2
                ? "Starting..."
                : "Starting...\nReady to accept connections";
          });

      // Act
      var result = await mock.Object.WaitForLogMessageAsync(
          "Ready to accept connections", timeout: 5000, cancellationToken: TestContext.Current.CancellationToken);

      // Assert
      Assert.True(result);
      Assert.True(callCount >= 2);
    }

    #endregion

    #region WaitForProcessAsync

    [Fact]
    public async Task WaitForProcessAsync_ProcessFound_ReturnsTrue()
    {
      // Arrange
      var mock = new Mock<IContainerService>();
      mock.Setup(s => s.Id).Returns("test-id");
      mock.Setup(s => s.ExecuteAsync(
              It.Is<string>(cmd => cmd.Contains("pgrep")),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync("1234\n");

      // Act
      var result = await mock.Object.WaitForProcessAsync("postgres", timeout: 5000, cancellationToken: TestContext.Current.CancellationToken);

      // Assert
      Assert.True(result);
      mock.Verify(
          s => s.ExecuteAsync("pgrep -f postgres", It.IsAny<CancellationToken>()),
          Times.AtLeastOnce);
    }

    [Fact]
    public async Task WaitForProcessAsync_ProcessNotFound_TimesOutReturnsFalse()
    {
      // Arrange
      var mock = new Mock<IContainerService>();
      mock.Setup(s => s.Id).Returns("test-id");
      mock.Setup(s => s.ExecuteAsync(
              It.Is<string>(cmd => cmd.Contains("pgrep")),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(string.Empty);

      // Act
      var result = await mock.Object.WaitForProcessAsync("nonexistent", timeout: 200, cancellationToken: TestContext.Current.CancellationToken);

      // Assert
      Assert.False(result);
    }

    [Fact]
    public async Task WaitForProcessAsync_WhitespaceResult_TimesOutReturnsFalse()
    {
      // Arrange
      var mock = new Mock<IContainerService>();
      mock.Setup(s => s.Id).Returns("test-id");
      mock.Setup(s => s.ExecuteAsync(
              It.Is<string>(cmd => cmd.Contains("pgrep")),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync("   \n  ");

      // Act
      var result = await mock.Object.WaitForProcessAsync("myprocess", timeout: 200, cancellationToken: TestContext.Current.CancellationToken);

      // Assert
      Assert.False(result);
    }

    [Fact]
    public async Task WaitForProcessAsync_ExecuteThrows_ContinuesRetrying()
    {
      // Arrange - first call throws, second returns PID
      var callCount = 0;
      var mock = new Mock<IContainerService>();
      mock.Setup(s => s.Id).Returns("test-id");
      mock.Setup(s => s.ExecuteAsync(
              It.Is<string>(cmd => cmd.Contains("pgrep")),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(() =>
          {
            callCount++;
            if (callCount == 1)
              throw new System.InvalidOperationException("Connection refused");
            return "5678";
          });

      // Act
      var result = await mock.Object.WaitForProcessAsync("nginx", timeout: 5000, cancellationToken: TestContext.Current.CancellationToken);

      // Assert
      Assert.True(result);
      Assert.True(callCount >= 2);
    }

    [Fact]
    public async Task WaitForProcessAsync_ProcessAppearsOnSecondPoll_ReturnsTrue()
    {
      // Arrange - first call returns empty, second returns PID
      var callCount = 0;
      var mock = new Mock<IContainerService>();
      mock.Setup(s => s.Id).Returns("test-id");
      mock.Setup(s => s.ExecuteAsync(
              It.Is<string>(cmd => cmd.Contains("pgrep")),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(() =>
          {
            callCount++;
            return callCount < 2 ? "" : "9999";
          });

      // Act
      var result = await mock.Object.WaitForProcessAsync("redis-server", timeout: 5000, cancellationToken: TestContext.Current.CancellationToken);

      // Assert
      Assert.True(result);
      Assert.True(callCount >= 2);
    }

    #endregion
  }
}
