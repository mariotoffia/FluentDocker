using System.Formats.Tar;
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
using FluentDocker.Tests.Mocks;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Service
{
  [Trait("Category", "Unit")]
  public class ContainerServiceProductionReadinessTests : MockKernelTestBase, IAsyncLifetime
  {
    public async ValueTask InitializeAsync()
    {
      await InitializeMockKernelAsync();
    }

    [Fact]
    public async Task StartAsync_WhenContainerExitsImmediately_LeavesStateStopped()
    {
      MockPack
          .SetupContainerStart()
          .SetupContainerInspect("container-123", running: false);
      var service = new ContainerService(Kernel, DriverId, "container-123", "alpine", "test");

      await service.StartAsync(TestContext.Current.CancellationToken);

      Assert.Equal(ServiceRunningState.Stopped, service.State);
    }

    [Fact]
    public async Task ExportOnDispose_WithExplode_ExtractsArchiveToDirectory()
    {
      var sourceDir = Path.Combine(".out", "export-source");
      var tarPath = Path.Combine(".out", "export.tar");
      var destinationDir = Path.Combine(".out", "export-destination");
      Directory.CreateDirectory(sourceDir);
      Directory.CreateDirectory(Path.GetDirectoryName(tarPath)!);
      if (Directory.Exists(destinationDir))
        Directory.Delete(destinationDir, recursive: true);
      if (File.Exists(tarPath))
        File.Delete(tarPath);
      await File.WriteAllTextAsync(Path.Combine(sourceDir, "file.txt"), "data",
          TestContext.Current.CancellationToken);
      TarFile.CreateFromDirectory(sourceDir, tarPath, includeBaseDirectory: false);
      var archive = await File.ReadAllBytesAsync(tarPath, TestContext.Current.CancellationToken);

      MockPack
          .SetupContainerRemove();
      MockPack.ContainerDriver
          .Setup(d => d.ExportAsync(
              It.IsAny<DriverContext>(),
              "container-123",
              It.IsAny<string>(),
              It.IsAny<CancellationToken>()))
          .Callback<DriverContext, string, string, CancellationToken>((_, _, path, _) =>
              File.WriteAllBytes(path, archive))
          .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));
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
              Type = LifecycleHookType.Export,
              TriggerState = ServiceRunningState.Removing,
              HostPath = destinationDir,
              Explode = true,
              Condition = _ => true
            }
          ]);

      await service.RemoveAsync(cancellationToken: TestContext.Current.CancellationToken);

      Assert.Equal("data", await File.ReadAllTextAsync(
          Path.Combine(destinationDir, "file.txt"),
          TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExecuteAsync_WhenDriverFails_PreservesErrorContext()
    {
      var context = new ErrorContext("ExecContainer") { DriverId = DriverId };
      MockPack.ContainerDriver
          .Setup(d => d.ExecAsync(
              It.IsAny<DriverContext>(),
              "container-123",
              It.IsAny<ExecConfig>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<ExecResult>.Fail(
              "exec failed",
              ErrorCodes.Container.ExecFailed,
              context));
      var service = new ContainerService(Kernel, DriverId, "container-123", "alpine", "test");

      var error = await Assert.ThrowsAsync<DriverException>(() =>
          service.ExecuteAsync("false", TestContext.Current.CancellationToken));

      Assert.Same(context, error.Context);
    }

    [Fact]
    public async Task ExportOnDispose_WithExplodeAndInvalidArchive_Propagates()
    {
      MockPack
          .SetupContainerRemove();
      MockPack.ContainerDriver
          .Setup(d => d.ExportAsync(
              It.IsAny<DriverContext>(),
              "container-123",
              It.IsAny<string>(),
              It.IsAny<CancellationToken>()))
          .Callback<DriverContext, string, string, CancellationToken>((_, _, path, _) =>
              File.WriteAllBytes(path, [1, 2, 3]))
          .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));
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
              Type = LifecycleHookType.Export,
              TriggerState = ServiceRunningState.Removing,
              HostPath = Path.Combine(".out", "bad-export"),
              Explode = true,
              Condition = _ => true
            }
          ]);

      await Assert.ThrowsAsync<EndOfStreamException>(() =>
          service.RemoveAsync(cancellationToken: TestContext.Current.CancellationToken));
    }
  }
}
