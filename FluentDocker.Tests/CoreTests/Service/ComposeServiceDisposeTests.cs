using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using FluentDocker.Services.Impl;
using FluentDocker.Tests.Mocks;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Service
{
  [Trait("Category", "Unit")]
  public class ComposeServiceDisposeTests
  {
    [Fact]
    public async Task DisposeAsync_WithoutRemoveVolumes_DoesNotRemoveVolumes()
    {
      var mockPack = new MockDriverPack();
      mockPack.SetupComposeDown();
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = new ComposeService(
            kernel, "docker", ["docker-compose.yml"], "test-project",
            disposeCleanupTimeout: TimeSpan.FromMilliseconds(100));

        await service.DisposeAsync();

        mockPack.ComposeDriver.Verify(d => d.DownAsync(
            It.IsAny<DriverContext>(),
            It.Is<ComposeDownConfig>(c => !c.RemoveVolumes),
            It.IsAny<CancellationToken>()), Times.Once);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task DisposeAsync_WithRemoveVolumes_RemovesVolumes()
    {
      var mockPack = new MockDriverPack();
      mockPack.SetupComposeDown();
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = new ComposeService(
            kernel, "docker", ["docker-compose.yml"], "test-project",
            removeVolumes: true,
            disposeCleanupTimeout: TimeSpan.FromMilliseconds(100));

        await service.DisposeAsync();

        mockPack.ComposeDriver.Verify(d => d.DownAsync(
            It.IsAny<DriverContext>(),
            It.Is<ComposeDownConfig>(c => c.RemoveVolumes),
            It.IsAny<CancellationToken>()), Times.Once);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task DisposeAsync_WhenDownIgnoresCancellation_ReturnsAfterCleanupTimeout()
    {
      var never = new TaskCompletionSource<CommandResponse<Unit>>(
          TaskCreationOptions.RunContinuationsAsynchronously);
      var mockPack = new MockDriverPack();
      mockPack.ComposeDriver
          .Setup(d => d.DownAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<ComposeDownConfig>(),
              It.IsAny<CancellationToken>()))
          .Returns(never.Task);
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = new ComposeService(
            kernel, "docker", ["docker-compose.yml"], "test-project",
            disposeCleanupTimeout: TimeSpan.FromMilliseconds(50));

        var disposeTask = service.DisposeAsync().AsTask();

        await disposeTask.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
      }
      finally { kernel.Dispose(); }
    }
  }
}
