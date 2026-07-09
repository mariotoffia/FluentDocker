using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Drivers;
using FluentDocker.Model.Drivers;
using FluentDocker.Services;
using FluentDocker.Services.Impl;
using FluentDocker.Testing.Core;
using FluentDocker.Tests.Mocks;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Service
{
  [Trait("Category", "Unit")]
  public class ProdReadyServicesFixesTests : MockKernelTestBase, IAsyncLifetime
  {
    public async ValueTask InitializeAsync()
    {
      await InitializeMockKernelAsync();
    }

    [Fact]
    public async Task ComposeResource_DisposeAsync_WhenBorrowed_DoesNotStopOrDown()
    {
      MockPack.SetupComposeStop().SetupComposeDown();
      await using var resource = new ComposeResource(
          Kernel,
          compose => compose.WithProjectName("borrowed-project").ConnectToExisting(),
          new DockerResourceOptions
          {
            CleanupOrphansOnInit = false,
            EnableSessionLabels = false
          });

      await resource.InitializeAsync(TestContext.Current.CancellationToken);
      await resource.DisposeAsync();

      MockPack.ComposeDriver.Verify(d => d.StopAsync(
          It.IsAny<DriverContext>(),
          It.IsAny<ComposeStopConfig>(),
          It.IsAny<CancellationToken>()), Times.Never);
      MockPack.ComposeDriver.Verify(d => d.DownAsync(
          It.IsAny<DriverContext>(),
          It.IsAny<ComposeDownConfig>(),
          It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ComposeRemoveAsync_WhenBorrowed_DoesNotDown()
    {
      MockPack.SetupComposeDown();
      var service = new ComposeService(
          Kernel, DriverId, ["compose.yml"], "borrowed-project", downOnDispose: false);

      await service.RemoveAsync(cancellationToken: TestContext.Current.CancellationToken);

      Assert.Equal(ServiceRunningState.Removed, service.State);
      MockPack.ComposeDriver.Verify(d => d.DownAsync(
          It.IsAny<DriverContext>(),
          It.IsAny<ComposeDownConfig>(),
          It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ComposeRefreshStateAsync_ListsAllServices()
    {
      ComposeListConfig received = null!;
      MockPack.ComposeDriver
          .Setup(d => d.ListAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<ComposeListConfig>(),
              It.IsAny<CancellationToken>()))
          .Callback<DriverContext, ComposeListConfig, CancellationToken>((_, config, _) =>
              received = config)
          .ReturnsAsync(CommandResponse<IList<ComposeServiceInfo>>.Ok(
          [
            new ComposeServiceInfo { Name = "web", State = "exited" }
          ]));
      var service = new ComposeService(Kernel, DriverId, ["compose.yml"], "project");

      await service.RefreshStateAsync(TestContext.Current.CancellationToken);

      Assert.True(received.All);
      Assert.Equal(ServiceRunningState.Stopped, service.State);
    }

    [Fact]
    public async Task ComposeLifecycle_AfterRemove_DoesNotReenterDriver()
    {
      MockPack.SetupComposeDown().SetupComposeStop().SetupComposeStart();
      var service = new ComposeService(Kernel, DriverId, ["compose.yml"], "project");

      await service.RemoveAsync(cancellationToken: TestContext.Current.CancellationToken);
      await service.StopAsync(TestContext.Current.CancellationToken);
      await Assert.ThrowsAsync<InvalidOperationException>(() =>
          service.StartAsync(TestContext.Current.CancellationToken));

      Assert.Equal(ServiceRunningState.Removed, service.State);
      MockPack.ComposeDriver.Verify(d => d.DownAsync(
          It.IsAny<DriverContext>(),
          It.IsAny<ComposeDownConfig>(),
          It.IsAny<CancellationToken>()), Times.Once);
      MockPack.ComposeDriver.Verify(d => d.StopAsync(
          It.IsAny<DriverContext>(),
          It.IsAny<ComposeStopConfig>(),
          It.IsAny<CancellationToken>()), Times.Never);
      MockPack.ComposeDriver.Verify(d => d.StartAsync(
          It.IsAny<DriverContext>(),
          It.IsAny<ComposeFileConfig>(),
          It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PullImageAsync_DigestReference_ReturnsDigestFullName()
    {
      const string digest = "sha256:abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789";
      const string digestRef = "repo@" + digest;
      MockPack.SetupImagePull();
      MockPack.SetupImageInspect("sha256:pulleddigest", digestRef);
      var service = new HostService(Kernel, DriverId, "host");

      var image = await service.PullImageAsync(
          digestRef,
          cancellationToken: TestContext.Current.CancellationToken);

      Assert.Equal(digest, image.Tag);
      Assert.Equal(digestRef, image.FullName);
    }

    [Fact]
    public async Task ExportHook_WithExplode_ExtractsTarWrittenToDriverTempFile()
    {
      var sourceDir = Path.Combine(".out", "prod-ready-export-source");
      var tarPath = Path.Combine(".out", "prod-ready-export.tar");
      var destinationDir = Path.Combine(".out", "prod-ready-export-destination");
      Directory.CreateDirectory(sourceDir);
      Directory.CreateDirectory(Path.GetDirectoryName(tarPath)!);
      if (Directory.Exists(destinationDir))
        Directory.Delete(destinationDir, recursive: true);
      if (File.Exists(tarPath))
        File.Delete(tarPath);
      await File.WriteAllTextAsync(
          Path.Combine(sourceDir, "file.txt"),
          "data",
          TestContext.Current.CancellationToken);
      TarFile.CreateFromDirectory(sourceDir, tarPath, includeBaseDirectory: false);

      MockPack.SetupContainerRemove();
      MockPack.ContainerDriver
          .Setup(d => d.ExportAsync(
              It.IsAny<DriverContext>(),
              "container-123",
              It.IsAny<string>(),
              It.IsAny<CancellationToken>()))
          .Callback<DriverContext, string, string, CancellationToken>((_, _, path, _) =>
              File.Copy(tarPath, path, overwrite: true))
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
  }
}
