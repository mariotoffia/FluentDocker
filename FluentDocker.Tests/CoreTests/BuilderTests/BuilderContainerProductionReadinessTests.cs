using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Model.Containers;
using FluentDocker.Model.Drivers;
using FluentDocker.Services;
using FluentDocker.Services.Impl;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.BuilderTests
{
  [Trait("Category", "Unit")]
  public partial class BuilderContainerTests
  {
    [Fact]
    public async Task ExecuteOnRunning_ExecutesSingleArgvCommand()
    {
      MockPack
          .SetupContainerCreate("container-123")
          .SetupContainerStart()
          .SetupContainerInspect("container-123", running: true)
          .SetupContainerStop()
          .SetupContainerRemove()
          .SetupContainerExec();

      await new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c
              .UseImage("postgres:alpine")
              .ExecuteOnRunning("psql", "-U", "postgres", "-c", "select 1"))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);

      MockPack.ContainerDriver.Verify(d => d.ExecAsync(
          It.IsAny<DriverContext>(),
          "container-123",
          It.Is<ExecConfig>(cfg =>
              cfg.Command.Length == 5 &&
              cfg.Command[0] == "psql" &&
              cfg.Command[4] == "select 1"),
          It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CopyToOnStart_WithDirectory_CopiesDirectoryPath()
    {
      var sourceDir = Path.Combine(".out", "copy-to-on-start-dir");
      Directory.CreateDirectory(sourceDir);
      await File.WriteAllTextAsync(Path.Combine(sourceDir, "file.txt"), "data",
          TestContext.Current.CancellationToken);

      MockPack
          .SetupContainerCreate("container-123")
          .SetupContainerStart()
          .SetupContainerInspect("container-123", running: true)
          .SetupContainerStop()
          .SetupContainerRemove()
          .SetupContainerCopyTo();

      await new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c
              .UseImage("alpine")
              .CopyToOnStart(sourceDir, "/data"))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);

      MockPack.ContainerDriver.Verify(d => d.CopyToAsync(
          It.IsAny<DriverContext>(),
          "container-123",
          sourceDir,
          "/data",
          It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DestroyIfExists_WhenRemoveFails_ThrowsBeforeCreate()
    {
      var context = new ErrorContext("RemoveContainer") { DriverId = DriverId };
      MockPack
          .SetupContainerList(new Container { Id = "existing", Name = "web" });
      MockPack.ContainerDriver
          .Setup(d => d.RemoveAsync(
              It.IsAny<DriverContext>(),
              "existing",
              true,
              true,
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Fail(
              "remove failed",
              ErrorCodes.Container.RemoveFailed,
              context));

      var error = await Assert.ThrowsAsync<DriverException>(() => new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c
              .UseImage("nginx")
              .WithName("web")
              .DestroyIfExists(force: true, removeVolumes: true))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken));

      Assert.Same(context, error.Context);
      MockPack.ContainerDriver.Verify(d => d.CreateAsync(
          It.IsAny<DriverContext>(),
          It.IsAny<ContainerCreateConfig>(),
          It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteOnDisposing_ExecutesSingleArgvCommand()
    {
      MockPack
          .SetupContainerRemove()
          .SetupContainerExec();
      var service = new ContainerService(
          Kernel,
          DriverId,
          "container-123",
          "alpine",
          "test",
          stopOnDispose: false,
          deleteOnDispose: true,
          lifecycleHooks:
          [
            new LifecycleHook
            {
              Type = LifecycleHookType.Execute,
              TriggerState = ServiceRunningState.Removing,
              Command = ["sh", "-c", "echo bye"]
            }
          ]);

      await service.RemoveAsync(cancellationToken: TestContext.Current.CancellationToken);

      MockPack.ContainerDriver.Verify(d => d.ExecAsync(
          It.IsAny<DriverContext>(),
          "container-123",
          It.Is<ExecConfig>(cfg =>
              cfg.Command.Length == 3 &&
              cfg.Command[0] == "sh" &&
              cfg.Command[2] == "echo bye"),
          It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UseContainer_WhenStartedContainerAlreadyExited_BuildSucceedsWithoutRemoving()
    {
      MockPack
          .SetupContainerCreate("container-123")
          .SetupContainerStart()
          .SetupContainerInspect("container-123", running: false);

      var results = await new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c
              .UseImage("alpine")
              .WithCommand("echo", "hi"))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);

      Assert.Single(results.All);
      MockPack.ContainerDriver.Verify(d => d.RemoveAsync(
          It.IsAny<DriverContext>(),
          "container-123",
          true,
          false,
          It.IsAny<CancellationToken>()), Times.Never);
    }
  }
}
