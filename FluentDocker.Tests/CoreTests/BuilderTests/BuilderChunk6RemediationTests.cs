using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Podman;
using FluentDocker.Kernel;
using FluentDocker.Model.Containers;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Networks;
using FluentDocker.Model.Volumes;
using FluentDocker.Tests.Mocks;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.BuilderTests
{
  [Trait("Category", "Unit")]
  public partial class BuilderChunk6RemediationTests : MockKernelTestBase, IAsyncLifetime
  {
    public async ValueTask InitializeAsync()
    {
      await InitializeMockKernelAsync();
    }

    [Fact]
    public async Task BuildAsync_RetryAfterLinkedWaitFailure_RerunsDeferredWaits()
    {
      var createAttempt = 0;
      var frontendLogCalls = 0;
      MockPack
          .SetupContainerStart()
          .SetupContainerInspect("backend", running: true)
          .SetupContainerInspect("frontend", running: true)
          .SetupContainerRemove();
      MockPack.ContainerDriver
          .Setup(d => d.CreateAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<ContainerCreateConfig>(),
              It.IsAny<CancellationToken>()))
          .Returns<DriverContext, ContainerCreateConfig, CancellationToken>((_, cfg, _) =>
          {
            if (cfg.Name == "frontend")
              createAttempt++;
            return Task.FromResult(CommandResponse<ContainerCreateResult>.Ok(
                new ContainerCreateResult { Id = cfg.Name }));
          });
      MockPack.ContainerDriver
          .Setup(d => d.GetLogsAsync(
              It.IsAny<DriverContext>(), "frontend", false, It.IsAny<int?>(), false,
              It.IsAny<CancellationToken>()))
          .Returns(() =>
          {
            frontendLogCalls++;
            return Task.FromResult(CommandResponse<string>.Ok(
                createAttempt == 1 ? "starting" : "ready"));
          });
      var builder = new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c
              .UseImage("alpine")
              .WithName("backend"))
          .UseContainer(c => c
              .UseImage("alpine")
              .WithName("frontend")
              .WithLink("backend")
              .WithWaitPollInterval(1)
              .WaitForLogMessage("ready", 5));

      await Assert.ThrowsAsync<FluentDockerException>(() =>
          builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken));
      // Snapshot so the assertion below proves attempt 2 re-ran the deferred waits;
      // attempt 1 alone can produce multiple poll calls.
      var callsAfterFailedAttempt = frontendLogCalls;
      await using var results = await builder.BuildAsync(
          cancellationToken: TestContext.Current.CancellationToken);

      Assert.Equal(2, results.All.Count);
      Assert.True(frontendLogCalls > callsAfterFailedAttempt);
    }

    [Fact]
    public async Task BuildAsync_FailureRemovesBuilderCreatedNetworkAndVolume()
    {
      MockPack
          .SetupNetworkList()
          .SetupNetworkCreate("created-network")
          .SetupNetworkRemove()
          .SetupVolumeCreate("created-volume")
          .SetupVolumeRemove();
      MockPack.VolumeDriver
          .Setup(d => d.InspectAsync(
              It.IsAny<DriverContext>(), "created-volume", It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Volume>.Fail("missing"));
      MockPack.ContainerDriver
          .Setup(d => d.CreateAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<ContainerCreateConfig>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<ContainerCreateResult>.Fail("boom"));

      var ex = await Assert.ThrowsAsync<DriverException>(() => new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseNetwork(n => n.WithName("created-network"))
          .UseVolume(v => v.WithName("created-volume"))
          .UseContainer(c => c.UseImage("alpine"))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken));

      Assert.IsType<BuildFailureManifest>(ex.Data["BuildFailureManifest"]);
      MockPack.NetworkDriver.Verify(d => d.RemoveAsync(
          It.IsAny<DriverContext>(), "created-network", It.IsAny<CancellationToken>()),
          Times.Once);
      MockPack.VolumeDriver.Verify(d => d.RemoveAsync(
          It.IsAny<DriverContext>(), "created-volume", true, It.IsAny<CancellationToken>()),
          Times.Once);
    }

    [Fact]
    public async Task BuildAsync_WaitFailureWithKeepContainer_AddsKeptContainerToManifest()
    {
      MockPack
          .SetupContainerCreate("kept-container")
          .SetupContainerStart()
          .SetupContainerInspect("kept-container", running: true)
          .SetupContainerRemove()
          .SetupContainerGetLogs("tail");

      var ex = await Assert.ThrowsAsync<FluentDockerException>(() => new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c
              .UseImage("alpine")
              .WithName("kept")
              .KeepContainer()
              .WithWaitPollInterval(1)
              .WaitForLogMessage("never", 5))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken));
      var manifest = Assert.IsType<BuildFailureManifest>(ex.Data["BuildFailureManifest"]);

      Assert.Contains(manifest.KeptResources, r =>
          r.Kind == "container" && r.Id == "kept-container" && r.Reason == "KeepContainer()");
    }

    [Fact]
    public async Task BuildAsync_FailureLeavesBorrowedNetworkAndVolumeUntouched()
    {
      MockPack
          .SetupNetworkList(new Network { Id = "borrowed-network", Name = "shared-net" })
          .SetupNetworkRemove()
          .SetupVolumeRemove();
      MockPack.VolumeDriver
          .Setup(d => d.InspectAsync(
              It.IsAny<DriverContext>(), "shared-vol", It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Volume>.Ok(new Volume
          {
            Name = "shared-vol",
            Driver = "local"
          }));
      MockPack.ContainerDriver
          .Setup(d => d.CreateAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<ContainerCreateConfig>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<ContainerCreateResult>.Fail("boom"));

      await Assert.ThrowsAsync<DriverException>(() => new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseNetwork(n => n.WithName("shared-net"))
          .UseVolume(v => v.WithName("shared-vol"))
          .UseContainer(c => c.UseImage("alpine"))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken));

      MockPack.NetworkDriver.Verify(d => d.RemoveAsync(
          It.IsAny<DriverContext>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
          Times.Never);
      MockPack.VolumeDriver.Verify(d => d.RemoveAsync(
          It.IsAny<DriverContext>(), It.IsAny<string>(), It.IsAny<bool>(),
          It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task BuildAsync_EmptyBuild_Throws()
    {
      var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => new Builder()
          .WithinDriver(DriverId, Kernel)
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken));

      Assert.Equal("no resources configured", ex.Message);
    }

    [Fact]
    public async Task BuildAsync_ConcurrentCall_Throws()
    {
      var enteredCreate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      var releaseCreate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      MockPack
          .SetupContainerStart()
          .SetupContainerInspect("container-123", running: true)
          .SetupContainerStop()
          .SetupContainerRemove();
      MockPack.ContainerDriver
          .Setup(d => d.CreateAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<ContainerCreateConfig>(),
              It.IsAny<CancellationToken>()))
          .Returns(async () =>
          {
            enteredCreate.SetResult();
            await releaseCreate.Task.ConfigureAwait(false);
            return CommandResponse<ContainerCreateResult>.Ok(
                new ContainerCreateResult { Id = "container-123" });
          });
      var builder = new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c.UseImage("alpine"));
      var firstBuild = builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken);
      await enteredCreate.Task.WaitAsync(TestContext.Current.CancellationToken);

      var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
          builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken));
      releaseCreate.SetResult();
      await using var results = await firstBuild;

      Assert.Equal("BuildAsync is already running on this Builder instance", ex.Message);
      Assert.Single(results.All);
    }

    [Theory]
    [InlineData("UseContainer")]
    [InlineData("UseNetwork")]
    [InlineData("UseVolume")]
    [InlineData("UseCompose")]
    [InlineData("UsePod")]
    [InlineData("UseImage")]
    public void UseMethods_NullConfigure_ThrowArgumentNullException(string method)
    {
      var builder = new Builder().WithinDriver(DriverId, Kernel);

      var ex = Assert.Throws<ArgumentNullException>(() =>
      {
        switch (method)
        {
          case "UseContainer":
            builder.UseContainer(null!);
            break;
          case "UseNetwork":
            builder.UseNetwork(null!);
            break;
          case "UseVolume":
            builder.UseVolume(null!);
            break;
          case "UseCompose":
            builder.UseCompose(null!);
            break;
          case "UsePod":
            builder.UsePod(null!);
            break;
          case "UseImage":
            builder.UseImage("img", null!);
            break;
        }
      });

      Assert.Equal("configure", ex.ParamName);
    }

    [Fact]
    public async Task BuildAsync_NetworkWithoutName_ThrowsValidationError()
    {
      var ex = await Assert.ThrowsAsync<FluentDockerException>(() => new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseNetwork(n => n.UseDriver("bridge"))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken));

      Assert.Contains("Network name is required", ex.Message);
    }

    [Theory]
    [InlineData("8080", "0", "Port must be 1-65535")]
    [InlineData("-1", "80", "Port must be 0-65535")]
    [InlineData("70000", "80", "Port must be 0-65535")]
    [InlineData("999.9.9.9:8080", "80", "Host binding must be")]
    public async Task BuildAsync_PodWithInvalidPort_ThrowsValidationError(
        string hostPort, string containerPort, string expectedFragment)
    {
      var podDriver = new Mock<IPodmanPodDriver>();
      var pack = new MockDriverPack();
      pack.RegisterCustomDriver(podDriver.Object);
      await InitializeMockKernelAsync(pack, "podman");

      var ex = await Assert.ThrowsAsync<FluentDockerException>(() => new Builder()
          .WithinDriver(DriverId, Kernel)
          .UsePod(p => p.WithName("bad-pod").WithPort(hostPort, containerPort))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken));

      Assert.Contains(expectedFragment, ex.Message);
      podDriver.Verify(d => d.CreatePodAsync(
          It.IsAny<DriverContext>(), It.IsAny<PodCreateConfig>(),
          It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task BuildAsync_PodWithIpHostBindingAndRandomHostPort_PassesValidation()
    {
      var podDriver = new Mock<IPodmanPodDriver>();
      podDriver
          .Setup(d => d.CreatePodAsync(
              It.IsAny<DriverContext>(), It.IsAny<PodCreateConfig>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<PodCreateResult>.Ok(
              new PodCreateResult { Id = "pod-1" }));
      podDriver
          .Setup(d => d.StartPodAsync(
              It.IsAny<DriverContext>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));
      var pack = new MockDriverPack();
      pack.RegisterCustomDriver(podDriver.Object);
      await InitializeMockKernelAsync(pack, "podman");

      await using var results = await new Builder()
          .WithinDriver(DriverId, Kernel)
          .UsePod(p => p
              .WithName("good-pod")
              .WithPort("127.0.0.1:8080", "80")
              .WithPort("0", "443"))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);

      podDriver.Verify(d => d.CreatePodAsync(
          It.IsAny<DriverContext>(), It.IsAny<PodCreateConfig>(),
          It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ImageBuilder_RegistryPortAndTag_BuildsExpectedTag()
    {
      MockPack.ImageDriver
          .Setup(d => d.BuildAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<ImageBuildConfig>(),
              It.IsAny<IProgress<ImageBuildProgress>>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<ImageBuildResult>.Ok(new ImageBuildResult
          {
            ImageId = "image-123"
          }));
      var dockerfile = new ImageBuilder(Kernel, DriverId, "registry:5000/myapp:v1")
          .FromString("FROM scratch");
      dockerfile.WorkingFolder(".out/image-registry-tag-test");

      await using var image = await dockerfile.BuildAsync();

      MockPack.ImageDriver.Verify(d => d.BuildAsync(
          It.IsAny<DriverContext>(),
          It.Is<ImageBuildConfig>(cfg =>
              cfg.Tags.Count == 1 && cfg.Tags[0] == "registry:5000/myapp:v1"),
          It.IsAny<IProgress<ImageBuildProgress>>(),
          It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UseContainer_WithPort_UsesHostThenContainerOrderAndNormalizesContainerPort()
    {
      MockPack
          .SetupContainerCreate()
          .SetupContainerStart()
          .SetupContainerInspect(running: true)
          .SetupContainerStop()
          .SetupContainerRemove();

      await using var results = await new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c
              .UseImage("nginx")
              .WithPort("8080", "80"))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);

      MockPack.ContainerDriver.Verify(d => d.CreateAsync(
          It.IsAny<DriverContext>(),
          It.Is<ContainerCreateConfig>(cfg =>
              cfg.PortBindings.Count == 1 &&
              cfg.PortBindings["80/tcp"] == "8080"),
          It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task WaitForHttp_PortAndTimeout_UsesContainerPortOverload()
    {
      MockPack
          .SetupContainerCreate("http-container")
          .SetupContainerStart()
          .SetupContainerInspect("http-container", running: true)
          .SetupContainerRemove()
          .SetupContainerGetLogs("");

      var ex = await Assert.ThrowsAsync<FluentDockerException>(() => new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c
              .UseImage("nginx")
              .ExposePort("8080")
              .WithWaitPollInterval(1)
              .WaitForHttp("8080/tcp", 5))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken));

      Assert.Contains("Timeout waiting for HTTP", ex.Message);
    }

    [Fact]
    public async Task UseContainer_WithDuplicateHostVolumeAndReadOnly_PassesBothMounts()
    {
      MockPack
          .SetupContainerCreate()
          .SetupContainerStart()
          .SetupContainerInspect(running: true)
          .SetupContainerStop()
          .SetupContainerRemove();

      await using var results = await new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c
              .UseImage("nginx")
              .WithVolume("/host/data", "/app/data")
              .WithVolume("/host/data", "/app/ro", isReadOnly: true))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);

      MockPack.ContainerDriver.Verify(d => d.CreateAsync(
          It.IsAny<DriverContext>(),
          It.Is<ContainerCreateConfig>(cfg =>
              cfg.Volumes.Count == 2 &&
              cfg.Volumes.Contains("/host/data:/app/data") &&
              cfg.Volumes.Contains("/host/data:/app/ro:ro")),
          It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void UseContainer_WithContainerPathModeSuffix_Throws()
    {
      var ex = Assert.Throws<ArgumentException>(() => new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c
              .UseImage("nginx")
              .WithVolume("/host/data", "/app/data:ro")));

      Assert.Equal("containerPath", ex.ParamName);
    }
  }
}
