using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Model.Drivers;
using FluentDocker.Testing.Core;
using FluentDocker.Tests.Mocks;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Testing
{
  public class DockerAvailabilityTests
  {
    [Fact]
    [Trait("Category", "Unit")]
    public async Task IsAvailableAsync_WhenPingSucceeds_ReturnsTrue()
    {
      var (kernel, mockPack) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync();
      mockPack.SystemDriver
          .Setup(d => d.PingAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));

      var result = await DockerAvailability.IsAvailableAsync(
          () => Task.FromResult(kernel),
          "docker",
          TestContext.Current.CancellationToken);

      Assert.True(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task IsAvailableAsync_WhenPingFails_ReturnsFalse()
    {
      var (kernel, mockPack) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync();
      mockPack.SystemDriver
          .Setup(d => d.PingAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Fail("daemon unavailable"));

      var result = await DockerAvailability.IsAvailableAsync(
          () => Task.FromResult(kernel),
          "docker",
          TestContext.Current.CancellationToken);

      Assert.False(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task IsAvailableAsync_WhenRequestedDriverIsMissing_Throws()
    {
      var (kernel, _) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync();

      await Assert.ThrowsAsync<DriverNotFoundException>(
          () => DockerAvailability.IsAvailableAsync(
              () => Task.FromResult(kernel),
              "podman-cli",
              TestContext.Current.CancellationToken));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task IsAvailableAsync_WhenKernelFactoryThrows_ReturnsFalse()
    {
      var result = await DockerAvailability.IsAvailableAsync(
          () => throw new InvalidOperationException("factory failed"),
          "docker",
          TestContext.Current.CancellationToken);

      Assert.False(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task IsAvailableAsync_WhenCallerCancels_PropagatesCancellation()
    {
      var (kernel, mockPack) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync();
      mockPack.SystemDriver
          .Setup(d => d.PingAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<CancellationToken>()))
          .ThrowsAsync(new OperationCanceledException());
      using var cts = new CancellationTokenSource();
      cts.Cancel();

      await Assert.ThrowsAnyAsync<OperationCanceledException>(
          () => DockerAvailability.IsAvailableAsync(
              () => Task.FromResult(kernel),
              "docker",
              cts.Token));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task IsAvailableAsync_WhenDriverIdOmitted_UsesKernelDefaultDriver()
    {
      // Kernel's default driver is "podman-cli", NOT the historical hard-coded "docker-cli".
      var (kernel, mockPack) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("podman-cli");
      mockPack.SystemDriver
          .Setup(d => d.PingAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));

      // No driverId argument -> must resolve the probe kernel's own default driver.
      var result = await DockerAvailability.IsAvailableAsync(
          () => Task.FromResult(kernel),
          cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(result);
    }
  }
}
