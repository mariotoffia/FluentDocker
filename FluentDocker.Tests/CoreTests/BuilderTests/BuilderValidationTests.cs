using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Common;
using FluentDocker.Tests.Mocks;
using Xunit;

namespace FluentDocker.Tests.CoreTests.BuilderTests
{
  /// <summary>
  /// Tests for builder validation at build time.
  /// </summary>
  [Trait("Category", "Unit")]
  public class BuilderValidationTests : MockKernelTestBase, IAsyncLifetime
  {
    public async ValueTask InitializeAsync()
    {
      await InitializeMockKernelAsync();
    }

    [Fact]
    public async Task BuildAsync_MissingImage_ThrowsFluentDockerException()
    {
      MockPack
          .SetupContainerCreate()
          .SetupContainerStart()
          .SetupContainerInspect(running: true)
          .SetupContainerStop()
          .SetupContainerRemove();

      var ex = await Assert.ThrowsAsync<FluentDockerException>(async () =>
      {
        await new Builder()
            .WithinDriver(DriverId, Kernel)
            .UseContainer(c => c.WithName("test"))
            .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);
      });

      Assert.Contains("image is required", ex.Message);
    }

    [Fact]
    public async Task BuildAsync_AutoRemoveAndKeepContainer_ThrowsFluentDockerException()
    {
      MockPack
          .SetupContainerCreate()
          .SetupContainerStart()
          .SetupContainerInspect(running: true)
          .SetupContainerStop()
          .SetupContainerRemove();

      var ex = await Assert.ThrowsAsync<FluentDockerException>(async () =>
      {
        await new Builder()
            .WithinDriver(DriverId, Kernel)
            .UseContainer(c => c
                .UseImage("alpine")
                .WithAutoRemove()
                .KeepContainer())
            .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);
      });

      Assert.Contains("mutually exclusive", ex.Message);
    }

    [Fact]
    public async Task BuildAsync_AutoRemoveAndRestartPolicy_ThrowsFluentDockerException()
    {
      MockPack
          .SetupContainerCreate()
          .SetupContainerStart()
          .SetupContainerInspect(running: true)
          .SetupContainerStop()
          .SetupContainerRemove();

      var ex = await Assert.ThrowsAsync<FluentDockerException>(async () =>
      {
        await new Builder()
            .WithinDriver(DriverId, Kernel)
            .UseContainer(c => c
                .UseImage("alpine")
                .WithAutoRemove()
                .WithRestartPolicy("always"))
            .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);
      });

      Assert.Contains("mutually exclusive", ex.Message);
    }

    [Fact]
    public async Task BuildAsync_InvalidContainerPort_ThrowsFluentDockerException()
    {
      MockPack
          .SetupContainerCreate()
          .SetupContainerStart()
          .SetupContainerInspect(running: true)
          .SetupContainerStop()
          .SetupContainerRemove();

      var ex = await Assert.ThrowsAsync<FluentDockerException>(async () =>
      {
        await new Builder()
            .WithinDriver(DriverId, Kernel)
            .UseContainer(c => c
                .UseImage("alpine")
                .WithPort("99999/tcp", "8080"))
            .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);
      });

      Assert.Contains("Invalid container port", ex.Message);
    }

    [Fact]
    public async Task BuildAsync_ValidConfig_DoesNotThrow()
    {
      MockPack
          .SetupContainerCreate()
          .SetupContainerStart()
          .SetupContainerInspect(running: true)
          .SetupContainerStop()
          .SetupContainerRemove();

      await new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c
              .UseImage("alpine")
              .WithPort("80/tcp", "8080")
              .WithName("valid-test"))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task BuildAsync_AutoRemoveWithRestartPolicyNo_DoesNotThrow()
    {
      MockPack
          .SetupContainerCreate()
          .SetupContainerStart()
          .SetupContainerInspect(running: true)
          .SetupContainerStop()
          .SetupContainerRemove();

      await new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c
              .UseImage("alpine")
              .WithAutoRemove()
              .WithRestartPolicy("no"))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);
    }
  }
}
