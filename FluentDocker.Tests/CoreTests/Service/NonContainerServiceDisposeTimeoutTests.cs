using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Podman;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using FluentDocker.Services.Impl;
using FluentDocker.Tests.Mocks;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Service
{
  [Trait("Category", "Unit")]
  public class NonContainerServiceDisposeTimeoutTests
  {
    [Fact]
    public async Task NetworkDisposeAsync_WhenRemoveIgnoresCancellation_ReturnsAfterCleanupTimeout()
    {
      var mockPack = new MockDriverPack();
      mockPack.NetworkDriver
          .Setup(d => d.RemoveAsync(It.IsAny<DriverContext>(), "net-1", It.IsAny<CancellationToken>()))
          .Returns(new TaskCompletionSource<CommandResponse<Unit>>().Task);
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        await new NetworkService(
            kernel, "docker", "net-1", "net", true, TimeSpan.FromMilliseconds(50))
            .DisposeAsync().AsTask()
            .WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task VolumeDisposeAsync_WhenRemoveIgnoresCancellation_ReturnsAfterCleanupTimeout()
    {
      var mockPack = new MockDriverPack();
      mockPack.VolumeDriver
          .Setup(d => d.RemoveAsync(It.IsAny<DriverContext>(), "vol-1", true, It.IsAny<CancellationToken>()))
          .Returns(new TaskCompletionSource<CommandResponse<Unit>>().Task);
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        await new VolumeService(
            kernel, "docker", "vol-1", "local", true, TimeSpan.FromMilliseconds(50))
            .DisposeAsync().AsTask()
            .WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task PodDisposeAsync_WhenRemoveIgnoresCancellation_ReturnsAfterCleanupTimeout()
    {
      var podDriver = new Mock<IPodmanPodDriver>();
      podDriver
          .Setup(d => d.RemovePodAsync(It.IsAny<DriverContext>(), "pod-1", true, It.IsAny<CancellationToken>()))
          .Returns(new TaskCompletionSource<CommandResponse<Unit>>().Task);
      var mockPack = new MockDriverPack();
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      mockPack.RegisterCustomDriver(podDriver.Object);
      try
      {
        await new PodService(
            kernel, "docker", "pod-1", "pod-1", true, TimeSpan.FromMilliseconds(50))
            .DisposeAsync().AsTask()
            .WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
      }
      finally { kernel.Dispose(); }
    }
  }
}
