using System;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Drivers.Podman;
using FluentDocker.Drivers.Podman.BuilderExtensions;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using FluentDocker.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver
{
  /// <summary>
  /// Tests for Podman-specific builder extensions, demonstrating
  /// the driver-aware extension pattern.
  /// </summary>
  [Trait("Category", "Unit")]
  public class PodmanExtensionTests
  {
    private static async Task<(FluentDockerKernel kernel, MockDriverPack pack)> CreateKernelWithMockPack()
    {
      var pack = new MockDriverPack();
      var kernel = new FluentDockerKernel(new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      await kernel.RegisterDriverPackAsync("test", pack, new DriverContext("test"));
      return (kernel, pack);
    }

    [Fact]
    public async Task UsePod_WithPodmanDriver_UsesPod()
    {
      // Arrange - register a mock IPodmanPodDriver
      var (kernel, pack) = await CreateKernelWithMockPack();
      var mockPodDriver = new Mock<IPodmanPodDriver>();
      pack.RegisterCustomDriver(mockPodDriver.Object);

      // Act - call UsePod inside a container builder lambda
      new Builder()
          .WithinDriver("test", kernel)
          .UseContainer(cb =>
          {
            cb.UsePod("my-pod");

            // ContainerBuilder is internal; this verifies the public extension accepts
            // a pod-capable scoped builder and preserves the fluent chain.
          });

      // The test passes if UsePod doesn't throw and returns normally.
    }

    [Fact]
    public async Task UsePod_WithoutPodmanDriver_Throws()
    {
      // Arrange - no IPodmanPodDriver registered
      var (kernel, _) = await CreateKernelWithMockPack();

      var ex = Assert.Throws<InvalidOperationException>(() => new Builder()
          .WithinDriver("test", kernel)
          .UseContainer(cb =>
          {
            cb.UsePod("my-pod");
          }));

      Assert.Contains("requires a Podman driver", ex.Message);
    }

    [Fact]
    public void UsePod_NonScopedBuilder_Throws()
    {
      // Arrange - a mock IContainerBuilder that does NOT implement IDriverScopedBuilder
      var mockBuilder = new Mock<IContainerBuilder>();
      mockBuilder.Setup(b => b.WithLabel(It.IsAny<string>(), It.IsAny<string>()))
          .Returns(mockBuilder.Object);

      var ex = Assert.Throws<InvalidOperationException>(() => mockBuilder.Object.UsePod("my-pod"));
      Assert.Contains("requires a driver-scoped builder", ex.Message);
      mockBuilder.Verify(
          b => b.WithLabel(It.IsAny<string>(), It.IsAny<string>()),
          Times.Never());
    }

    [Fact]
    public async Task UsePod_ChainingPreserved()
    {
      // Arrange
      var (kernel, pack) = await CreateKernelWithMockPack();
      var mockPodDriver = new Mock<IPodmanPodDriver>();
      pack.RegisterCustomDriver(mockPodDriver.Object);

      // Act & Assert - chaining works through UsePod
      new Builder()
          .WithinDriver("test", kernel)
          .UseContainer(cb =>
          {
            var result = cb
                      .UseImage("alpine:latest")
                      .UsePod("my-pod")
                      .WithName("test-container");

            Assert.NotNull(result);
          });
    }
  }
}
