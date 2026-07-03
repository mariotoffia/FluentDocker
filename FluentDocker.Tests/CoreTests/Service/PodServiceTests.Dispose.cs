using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Model.Drivers;
using FluentDocker.Services;
using FluentDocker.Services.Impl;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Service
{
  [Trait("Category", "Unit")]
  public partial class PodServiceTests
  {
    #region Dispose Tests

    [Fact]
    public async Task DisposeAsync_WithRemoveOnDispose_RemovesPod()
    {
      // Arrange
      var (kernel, _, podDriver) = await CreateWithPodDriverAsync();

      podDriver
          .Setup(d => d.RemovePodAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              It.IsAny<bool>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));

      var service = new PodService(kernel, "docker", "pod-abc123", "my-pod",
          removeOnDispose: true);

      try
      {
        // Act
        await service.DisposeAsync();

        // Assert — RemovePodAsync should have been called with force: true
        podDriver.Verify(d => d.RemovePodAsync(
            It.IsAny<DriverContext>(),
            It.Is<string>(s => s == "my-pod"),
            It.Is<bool>(f => f == true),
            It.IsAny<CancellationToken>()), Times.Once);

        Assert.Equal(ServiceRunningState.Removed, service.State);
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    public async Task DisposeAsync_WithoutRemoveOnDispose_DoesNotRemovePod()
    {
      // Arrange
      var (kernel, _, podDriver) = await CreateWithPodDriverAsync();

      var service = new PodService(kernel, "docker", "pod-abc123", "my-pod",
          removeOnDispose: false);

      try
      {
        // Act
        await service.DisposeAsync();

        // Assert — RemovePodAsync should NOT have been called
        podDriver.Verify(d => d.RemovePodAsync(
            It.IsAny<DriverContext>(),
            It.IsAny<string>(),
            It.IsAny<bool>(),
            It.IsAny<CancellationToken>()), Times.Never);

        // State should remain Stopped (not Removed)
        Assert.Equal(ServiceRunningState.Stopped, service.State);
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    public async Task DisposeAsync_RemoveFailure_DoesNotThrow()
    {
      // Arrange
      var (kernel, _, podDriver) = await CreateWithPodDriverAsync();

      podDriver
          .Setup(d => d.RemovePodAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              It.IsAny<bool>(),
              It.IsAny<CancellationToken>()))
          .ThrowsAsync(new InvalidOperationException("pod removal failed"));

      var service = new PodService(kernel, "docker", "pod-abc123", "my-pod",
          removeOnDispose: true);

      try
      {
        // Act & Assert — DisposeAsync should swallow the exception
        var exception = await Record.ExceptionAsync(() => service.DisposeAsync().AsTask());
        Assert.Null(exception);
      }
      finally
      {
        kernel.Dispose();
      }
    }

    #endregion

  }
}
