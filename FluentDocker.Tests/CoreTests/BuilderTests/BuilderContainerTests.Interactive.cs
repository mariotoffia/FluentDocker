using System.Linq;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Drivers;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.BuilderTests
{
  /// <summary>
  /// Tests for interactive/tty/entrypoint builder options (issue #264): keeping a
  /// short-lived image alive for <c>ExecuteAsync</c> and overriding the entrypoint.
  /// </summary>
  public partial class BuilderContainerTests
  {
    #region Interactive / Tty / Entrypoint (issue #264)

    [Fact]
    public async Task UseContainer_WithInteractive_PassesInteractive()
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
              .UseImage("mcr.microsoft.com/dotnet/sdk:6.0")
              .WithInteractive())
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);

      MockPack.ContainerDriver.Verify(d => d.CreateAsync(
          It.IsAny<FluentDocker.Model.Drivers.DriverContext>(),
          It.Is<ContainerCreateConfig>(cfg => cfg.Interactive == true),
          It.IsAny<System.Threading.CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UseContainer_WithTty_PassesTty()
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
              .WithTty())
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);

      MockPack.ContainerDriver.Verify(d => d.CreateAsync(
          It.IsAny<FluentDocker.Model.Drivers.DriverContext>(),
          It.Is<ContainerCreateConfig>(cfg => cfg.Tty == true),
          It.IsAny<System.Threading.CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UseContainer_WithEntrypoint_PassesEntrypointTokens()
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
              .WithEntrypoint("/bin/sh", "-c"))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);

      MockPack.ContainerDriver.Verify(d => d.CreateAsync(
          It.IsAny<FluentDocker.Model.Drivers.DriverContext>(),
          It.Is<ContainerCreateConfig>(cfg =>
              cfg.Entrypoint != null &&
              cfg.Entrypoint.SequenceEqual(new[] { "/bin/sh", "-c" })),
          It.IsAny<System.Threading.CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UseContainer_WithoutInteractive_DefaultsToFalse()
    {
      MockPack
          .SetupContainerCreate()
          .SetupContainerStart()
          .SetupContainerInspect(running: true)
          .SetupContainerStop()
          .SetupContainerRemove();

      await new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c.UseImage("alpine"))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);

      MockPack.ContainerDriver.Verify(d => d.CreateAsync(
          It.IsAny<FluentDocker.Model.Drivers.DriverContext>(),
          It.Is<ContainerCreateConfig>(cfg =>
              cfg.Interactive == false && cfg.Tty == false && cfg.Entrypoint == null),
          It.IsAny<System.Threading.CancellationToken>()), Times.Once);
    }

    #endregion
  }
}
