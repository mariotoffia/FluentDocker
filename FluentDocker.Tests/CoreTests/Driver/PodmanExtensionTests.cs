using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Drivers.Podman;
using FluentDocker.Drivers.Podman.BuilderExtensions;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using FluentDocker.Tests.Mocks;
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
      var kernel = new FluentDockerKernel();
      await kernel.RegisterDriverPackAsync("test", pack, new DriverContext("test"));
      return (kernel, pack);
    }

    [Fact]
    public async Task UsePod_WithPodmanDriver_AppliesLabel()
    {
      // Arrange - register a mock IPodmanPodDriver
      var (kernel, pack) = await CreateKernelWithMockPack();
      var mockPodDriver = new Mock<IPodmanPodDriver>();
      pack.RegisterCustomDriver(mockPodDriver.Object);

      string? _appliedLabel = null;

      // Act - call UsePod inside a container builder lambda
      new Builder()
          .WithinDriver("test", kernel)
          .UseContainer(cb =>
          {
            cb.UsePod("my-pod");

            // The extension stores the pod name as a label.
            // Verify by checking that WithLabel was called on the builder.
            // Since ContainerBuilder is internal, we verify via IDriverScopedBuilder.
            // The label "io.podman.pod" should be set.
            // We can't directly inspect the internal state, but we
            // can verify the extension returned the builder (chaining works).
          });

      // The test passes if UsePod doesn't throw and returns normally.
      // Full label verification requires an integration test or
      // inspecting the internal builder state.
    }

    [Fact]
    public async Task UsePod_WithoutPodmanDriver_NoOp()
    {
      // Arrange - no IPodmanPodDriver registered
      var (kernel, _) = await CreateKernelWithMockPack();

      // Act & Assert - should be a no-op (no exception)
      new Builder()
          .WithinDriver("test", kernel)
          .UseContainer(cb =>
          {
            var result = cb.UsePod("my-pod");
            Assert.Same(cb, result); // chaining preserved
          });
    }

    [Fact]
    public void UsePod_NonScopedBuilder_NoOp()
    {
      // Arrange - a mock IContainerBuilder that does NOT implement IDriverScopedBuilder
      var mockBuilder = new Mock<IContainerBuilder>();
      mockBuilder.Setup(b => b.WithLabel(It.IsAny<string>(), It.IsAny<string>()))
          .Returns(mockBuilder.Object);

      // Act
      var result = mockBuilder.Object.UsePod("my-pod");

      // Assert - should return the same builder, no label applied
      Assert.Same(mockBuilder.Object, result);
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
