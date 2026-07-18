using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Podman;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using FluentDocker.Services;
using FluentDocker.Services.Extensions;
using FluentDocker.Services.Impl;
using FluentDocker.Tests.Mocks;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Service
{
  [Trait("Category", "Unit")]
  public class Chunk1ServiceOperationsProdReadinessTests : MockKernelTestBase, IAsyncLifetime
  {
    public async ValueTask InitializeAsync()
    {
      await InitializeMockKernelAsync();
    }

    [Fact]
    public async Task ComposeStartAsync_AfterDispose_ThrowsObjectDisposedException()
    {
      MockPack.SetupComposeStart();
      var service = new ComposeService(Kernel, DriverId, ["compose.yml"], "project", downOnDispose: false);
      await service.DisposeAsync();

      await Assert.ThrowsAsync<ObjectDisposedException>(() =>
          service.StartAsync(TestContext.Current.CancellationToken));

      MockPack.ComposeDriver.Verify(d => d.StartAsync(
          It.IsAny<DriverContext>(), It.IsAny<ComposeFileConfig>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PodStartAsync_AfterDispose_ThrowsObjectDisposedException()
    {
      var podDriver = new Mock<IPodmanPodDriver>();
      podDriver
          .Setup(d => d.StartPodAsync(It.IsAny<DriverContext>(), "pod", It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));
      MockPack.RegisterCustomDriver(podDriver.Object);
      var service = new PodService(Kernel, DriverId, "pod-id", "pod");
      await service.DisposeAsync();

      await Assert.ThrowsAsync<ObjectDisposedException>(() =>
          service.StartAsync(TestContext.Current.CancellationToken));

      podDriver.Verify(d => d.StartPodAsync(
          It.IsAny<DriverContext>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NetworkInspectAsync_AfterDispose_ThrowsObjectDisposedException()
    {
      MockPack.SetupNetworkInspect("net-1");
      var service = new NetworkService(Kernel, DriverId, "net-1", "net");
      await service.DisposeAsync();

      await Assert.ThrowsAsync<ObjectDisposedException>(() =>
          service.InspectAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task VolumeInspectAsync_AfterDispose_ThrowsObjectDisposedException()
    {
      MockPack.SetupVolumeInspect("vol-1");
      var service = new VolumeService(Kernel, DriverId, "vol-1", "local");
      await service.DisposeAsync();

      await Assert.ThrowsAsync<ObjectDisposedException>(() =>
          service.InspectAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ImagePushAsync_AfterDispose_ThrowsObjectDisposedException()
    {
      var service = new ImageService(Kernel, DriverId, "img-1", "repo", "tag");
      await service.DisposeAsync();

      await Assert.ThrowsAsync<ObjectDisposedException>(() =>
          service.PushAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task EngineScopeUseLinuxAsync_AfterDispose_ThrowsObjectDisposedException()
    {
      MockPack.SetupSystemIsWindowsEngine(true).SetupSystemSwitchToLinux();
      var scope = await EngineScope.CreateAsync(
          Kernel, DriverId, EngineScopeType.Windows, TestContext.Current.CancellationToken);
      await scope.DisposeAsync();

      await Assert.ThrowsAsync<ObjectDisposedException>(() =>
          scope.UseLinuxAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetImagesAsync_WithPortQualifiedRegistry_PreservesPushTarget()
    {
      MockPack.ImageDriver
          .Setup(d => d.ListAsync(
              It.IsAny<DriverContext>(), It.IsAny<ImageListFilter>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<IList<Image>>.Ok(
          [
            new Image { Id = "sha256:abc", RepoTags = ["localhost:5000/acme/api:1.2"] }
          ]));
      string pushed = null!;
      MockPack.ImageDriver
          .Setup(d => d.PushAsync(
              It.IsAny<DriverContext>(), It.IsAny<string>(), It.IsAny<IProgress<ImagePushProgress>>(),
              It.IsAny<CancellationToken>()))
          .Callback<DriverContext, string, IProgress<ImagePushProgress>, CancellationToken>((_, image, _, _) =>
              pushed = image)
          .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));
      var host = new HostService(Kernel, DriverId, "host");

      var image = Assert.Single(await host.GetImagesAsync(cancellationToken: TestContext.Current.CancellationToken));
      await image.PushAsync(cancellationToken: TestContext.Current.CancellationToken);

      Assert.Equal("localhost:5000/acme/api:1.2", image.FullName);
      Assert.Equal("localhost:5000/acme/api:1.2", pushed);
    }

    [Fact]
    public async Task BuildImageAsync_WithPortQualifiedRegistry_PreservesFullName()
    {
      MockPack.ImageDriver
          .Setup(d => d.BuildAsync(
              It.IsAny<DriverContext>(), It.IsAny<ImageBuildConfig>(), It.IsAny<IProgress<ImageBuildProgress>>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<ImageBuildResult>.Ok(new ImageBuildResult { ImageId = "sha256:built" }));
      var host = new HostService(Kernel, DriverId, "host");

      var image = await host.BuildImageAsync(
          new ImageBuildConfig
          {
            BuildContext = ".",
            Tags = ["localhost:5000/acme/api:1.2"]
          },
          cancellationToken: TestContext.Current.CancellationToken);

      Assert.Equal("localhost:5000/acme/api:1.2", image.FullName);
    }

    [Fact]
    public async Task WaitHelpers_WhenTokenIsCanceledDuringZeroDelayPoll_ThrowOperationCanceledException()
    {
      await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
      {
        using var cts = new CancellationTokenSource();
        var service = new Mock<IContainerService>();
        service
            .Setup(s => s.ExecuteAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
              cts.Cancel();
              return string.Empty;
            });
        await service.Object.WaitForProcessAsync("nginx", 1000, 0, cts.Token);
      });

      await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
      {
        using var cts = new CancellationTokenSource();
        var service = new Mock<IContainerService>();
        service
            .Setup(s => s.GetLogsAsync(false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
              cts.Cancel();
              return "booting";
            });
        await service.Object.WaitForLogMessageAsync("ready", 1000, 0, cts.Token);
      });

      await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
      {
        using var cts = new CancellationTokenSource();
        var service = new Mock<IContainerService>();
        service
            .Setup(s => s.ToHostExposedEndpointAsync("80/tcp", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
              cts.Cancel();
              return null!;
            });
        await service.Object.WaitForPortAsync("80/tcp", 1000, 0, cts.Token);
      });

      await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
      {
        using var cts = new CancellationTokenSource();
        var service = new Mock<IContainerService>();
        service
            .Setup(s => s.ToHostExposedEndpointAsync("80/tcp", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
              cts.Cancel();
              return null!;
            });
        await service.Object.WaitForHttpAsync("80/tcp", "/", 1000, 0, cts.Token);
      });
    }

    [Fact]
    public async Task WaitForProcessAsync_WhenFatalExceptionOccurs_PropagatesImmediately()
    {
      var service = new Mock<IContainerService>();
      service
          .Setup(s => s.ExecuteAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
#pragma warning disable CA2201 // Deliberate: proves a runtime-reserved fatal exception propagates unwrapped.
          .ThrowsAsync(new NullReferenceException("bug"));
#pragma warning restore CA2201

      await Assert.ThrowsAsync<NullReferenceException>(() =>
          service.Object.WaitForProcessAsync(
              "nginx",
              timeout: 1000,
              pollIntervalMs: 0,
              cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WaitForLogMessageAsync_WhenObjectDisposedOccurs_PropagatesImmediately()
    {
      var service = new Mock<IContainerService>();
      service
          .Setup(s => s.GetLogsAsync(false, It.IsAny<CancellationToken>()))
          .ThrowsAsync(new ObjectDisposedException("container"));

      await Assert.ThrowsAsync<ObjectDisposedException>(() =>
          service.Object.WaitForLogMessageAsync(
              "ready",
              timeout: 1000,
              pollIntervalMs: 0,
              cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ComposeRestartAsync_WhenCanceled_SetsStateUnknown()
    {
      MockPack.ComposeDriver
          .Setup(d => d.RestartAsync(
              It.IsAny<DriverContext>(), It.IsAny<ComposeRestartConfig>(), It.IsAny<CancellationToken>()))
          .ThrowsAsync(new OperationCanceledException(TestContext.Current.CancellationToken));
      var service = new ComposeService(Kernel, DriverId, ["compose.yml"], "project");

      await Assert.ThrowsAsync<OperationCanceledException>(() =>
          service.RestartAsync(TestContext.Current.CancellationToken));

      Assert.Equal(ServiceRunningState.Unknown, service.State);
    }

    [Fact]
    public async Task EngineScopeCreateAsync_WhenSwitchFails_IncludesDriverError()
    {
      MockPack.SetupSystemIsWindowsEngine(true);
      MockPack.SystemDriver
          .Setup(d => d.SwitchToLinuxDaemonAsync(It.IsAny<DriverContext>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Fail("daemon said no"));

      var error = await Assert.ThrowsAsync<DriverException>(() =>
          EngineScope.CreateAsync(Kernel, DriverId, EngineScopeType.Linux, TestContext.Current.CancellationToken));

      Assert.Contains("daemon said no", error.Message);
    }

    [Fact]
    public async Task ServiceEndpointResolver_DockerHostDnsCache_ExpiresStaleEntries()
    {
      var resolver = typeof(ServiceExtensions).Assembly.GetType(
          "FluentDocker.Services.Extensions.ServiceEndpointResolver")!;
      var cache = resolver.GetField(
          "DockerHostAddressCache",
          BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
      cache.GetType().GetMethod("Clear")!.Invoke(cache, []);
      const string Host = "localhost";
      var stale = IPAddress.Parse("192.0.2.123");
      var valueType = cache.GetType().GetGenericArguments()[1];
      var staleValue = valueType == typeof(IPAddress)
          ? stale
          : Activator.CreateInstance(valueType, stale, DateTimeOffset.UtcNow - TimeSpan.FromSeconds(1));
      cache.GetType().GetMethod("TryAdd")!.Invoke(cache, [Host, staleValue]);
      var method = resolver.GetMethod(
          "ResolveDockerHostAddressAsync",
          BindingFlags.NonPublic | BindingFlags.Static)!;

      var resolved = await (Task<IPAddress>)method.Invoke(
          null,
          [new Uri($"tcp://{Host}:2376"), TestContext.Current.CancellationToken])!;

      Assert.NotEqual(stale, resolved);
    }

    [Fact]
    public void SourceDocs_DoNotClaimRunningIncludesHealthAndDocumentLinuxGatewayFallback()
    {
      var root = FindRepositoryRoot();
      var stateDoc = File.ReadAllText(Path.Combine(root, "FluentDocker", "Services", "ServiceRunningState.cs"));
      var environmentDoc = File.ReadAllText(Path.Combine(
          root,
          "FluentDocker",
          "Services",
          "Extensions",
          "EnvironmentExtensions.cs"));

      Assert.DoesNotContain("running and healthy", stateDoc, StringComparison.OrdinalIgnoreCase);
      Assert.Contains("172.17.0.1", environmentDoc, StringComparison.Ordinal);
      Assert.Contains("bridge gateway convention", environmentDoc, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepositoryRoot()
    {
      var directory = new DirectoryInfo(AppContext.BaseDirectory);
      while (directory != null && !File.Exists(Path.Combine(directory.FullName, "FluentDocker.sln")))
        directory = directory.Parent;

      return directory?.FullName ?? throw new InvalidOperationException("Could not locate repository root.");
    }
  }
}
