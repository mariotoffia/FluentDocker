using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Builders.Compose;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Podman;
using FluentDocker.Kernel;
using FluentDocker.Model.Containers;
using FluentDocker.Model.Drivers;
using FluentDocker.Services;
using FluentDocker.Tests.Mocks;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.BuilderTests
{
  [Trait("Category", "Unit")]
  public class BuilderChunk5HardeningTests : MockKernelTestBase, IAsyncLifetime
  {
    public async ValueTask InitializeAsync()
    {
      await InitializeMockKernelAsync();
    }

    [Fact]
    public async Task BuildAsync_DoesNotRestartCleanExitedNonLinkedContainerDuringDeferredStartPass()
    {
      MockPack.ContainerDriver
          .Setup(d => d.CreateAsync(
              It.IsAny<DriverContext>(),
              It.Is<ContainerCreateConfig>(c => c.Name == "oneshot"),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<ContainerCreateResult>.Ok(
              new ContainerCreateResult { Id = "oneshot-id" }));
      MockPack.ContainerDriver
          .Setup(d => d.CreateAsync(
              It.IsAny<DriverContext>(),
              It.Is<ContainerCreateConfig>(c => c.Name == "linked"),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<ContainerCreateResult>.Ok(
              new ContainerCreateResult { Id = "linked-id" }));
      MockPack.SetupContainerStart().SetupContainerRemove().SetupContainerGetLogs("");
      MockPack.ContainerDriver
          .Setup(d => d.InspectAsync(
              It.IsAny<DriverContext>(), "oneshot-id", It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Container>.Ok(new Container
          {
            Id = "oneshot-id",
            State = new ContainerState { Running = false, Status = "exited", ExitCode = 0 }
          }));
      MockPack.ContainerDriver
          .Setup(d => d.InspectAsync(
              It.IsAny<DriverContext>(), "linked-id", It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Container>.Ok(new Container
          {
            Id = "linked-id",
            State = new ContainerState { Running = true, Status = "running" }
          }));

      await using var results = await new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c.UseImage("alpine").WithName("oneshot"))
          .UseContainer(c => c.UseImage("alpine").WithName("linked").WithLink("oneshot"))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);

      MockPack.ContainerDriver.Verify(d => d.StartAsync(
          It.IsAny<DriverContext>(), "oneshot-id", It.IsAny<CancellationToken>()), Times.Once);
      MockPack.ContainerDriver.Verify(d => d.StartAsync(
          It.IsAny<DriverContext>(), "linked-id", It.IsAny<CancellationToken>()), Times.Once);
      Assert.Equal(2, results.Containers.Count);
    }

    [Fact]
    public async Task BuildAsync_ContainerReferencingLaterNetworkThrowsClearOrderingError()
    {
      var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c.UseImage("nginx").WithName("web").WithNetwork("db-net"))
          .UseNetwork(n => n.WithName("db-net"))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken));

      Assert.Contains("container 'web'", ex.Message);
      Assert.Contains("network 'db-net'", ex.Message);
      Assert.Contains("declared after it", ex.Message);
    }

    [Fact]
    public async Task UseCompose_WhenUpThrows_CleansPartialStackAndDeletesModelOverlay()
    {
      string? overlayPath = null;
      MockPack.ComposeDriver
          .Setup(d => d.UpAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<ComposeUpConfig>(),
              It.IsAny<CancellationToken>()))
          .Callback<DriverContext, ComposeUpConfig, CancellationToken>((_, cfg, _) =>
          {
            overlayPath = cfg.ComposeFiles.Single(f => f.Contains("fluentdocker-models", StringComparison.Ordinal));
            Assert.True(File.Exists(overlayPath));
          })
          .ThrowsAsync(new OperationCanceledException("cancelled by daemon"));
      // self-created project: the ownership probe (compose ls) finds nothing, so up-failure triggers a best-effort down.
      MockPack.SetupComposeList();
      MockPack.SetupComposeDown();

      await Assert.ThrowsAsync<OperationCanceledException>(() => new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseCompose(c => c
              .WithComposeFile("docker-compose.yml")
              .WithProjectName("modelapp")
              .WithModels(m => m.AddModel("llm", s => s.WithModel("ai/smollm2"))))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken));

      Assert.False(File.Exists(overlayPath));
      MockPack.ComposeDriver.Verify(d => d.DownAsync(
          It.IsAny<DriverContext>(),
          It.Is<ComposeDownConfig>(c => c.ProjectName == "modelapp"),
          It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task WaitForHttpUrl_WhenContinuationReturnsZero_WaitsPollInterval()
    {
      MockPack
          .SetupContainerCreate("container-123")
          .SetupContainerStart()
          .SetupContainerInspect("container-123", running: true)
          .SetupContainerRemove()
          .SetupContainerGetLogs("");

      using var listener = LoopbackHttpListenerSupport.Start(out var baseUrl);
      var requests = 0;
      var server = Task.Run(async () =>
      {
        while (requests < 3)
        {
          var context = await listener.GetContextAsync();
          requests++;
          context.Response.StatusCode = 503;
          context.Response.Close();
        }
      });

      var elapsed = Stopwatch.StartNew();
      await using var results = await new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c
              .UseImage("alpine")
              .WithWaitPollInterval(50)
              .WaitForHttpUrl(
                  baseUrl,
                  timeoutMs: 2000,
                  continuation: (_, iteration) => iteration < 2 ? 0 : -1))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);
      elapsed.Stop();

      await server.ConfigureAwait(false);
      Assert.Single(results.Containers);
      Assert.Equal(3, requests);
      Assert.True(elapsed.ElapsedMilliseconds >= 80, $"Expected poll delay, elapsed {elapsed.ElapsedMilliseconds}ms.");
    }

    [Fact]
    public async Task WaitForHealthy_WhenContainerReportsUnhealthy_ThrowsDistinctMessage()
    {
      MockPack
          .SetupContainerCreate("container-123")
          .SetupContainerStart()
          .SetupContainerRemove()
          .SetupContainerGetLogs("");
      MockPack.ContainerDriver
          .Setup(d => d.InspectAsync(
              It.IsAny<DriverContext>(), "container-123", It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Container>.Ok(new Container
          {
            Id = "container-123",
            State = new ContainerState
            {
              Running = true,
              Status = "running",
              Health = new Health { Status = HealthState.Unhealthy }
            }
          }));

      var ex = await Assert.ThrowsAsync<FluentDockerException>(() => new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c.UseImage("alpine").WaitForHealthy(50))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken));

      Assert.Contains("reported Unhealthy", ex.Message);
      Assert.DoesNotContain("Timeout waiting", ex.Message);
    }

    [Theory]
    [InlineData("BAD=KEY", "value", "Environment key")]
    [InlineData("OK", "value", null)]
    public async Task BuildAsync_ValidatesEnvironmentKeys(string key, string value, string? expectedMessage)
    {
      var builder = new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c.UseImage("alpine").WithEnvironment(key, value));

      if (expectedMessage == null)
      {
        MockPack.SetupContainerCreate().SetupContainerStart().SetupContainerInspect(running: true).SetupContainerRemove();
        await using var results = await builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Single(results.Containers);
        return;
      }

      var ex = await Assert.ThrowsAsync<FluentDockerException>(() =>
          builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken));
      Assert.Contains(expectedMessage, ex.Message);
    }

    [Theory]
    [InlineData("+8080", "80/tcp", "Invalid host port")]
    [InlineData("8080", "+80/tcp", "Invalid container port")]
    [InlineData(" 8080", "80/tcp", "Invalid host port")]
    [InlineData("8080", " 80/tcp", "Invalid container port")]
    public async Task BuildAsync_RejectsNonCanonicalPortNumbers(
        string hostPort, string containerPort, string expectedMessage)
    {
      var ex = await Assert.ThrowsAsync<FluentDockerException>(() => new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c.UseImage("alpine").WithPort(hostPort, containerPort))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken));

      Assert.Contains(expectedMessage, ex.Message);
    }

    [Theory]
    [InlineData("bad/name")]
    [InlineData("-bad")]
    [InlineData("x")]
    public async Task BuildAsync_RejectsInvalidContainerName(string name)
    {
      var ex = await Assert.ThrowsAsync<FluentDockerException>(() => new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c.UseImage("alpine").WithName(name))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken));

      Assert.Contains("Invalid container name", ex.Message);
    }

    [Theory]
    [InlineData("./data")]
    [InlineData("data/files")]
    public async Task BuildAsync_RejectsRelativeBindMountSource(string hostPath)
    {
      var ex = await Assert.ThrowsAsync<FluentDockerException>(() => new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c.UseImage("alpine").WithVolume(hostPath, "/data"))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken));

      Assert.Contains("Relative bind mount source", ex.Message);
    }

    [Fact]
    public async Task FailureManifest_IncludesBorrowedReusedContainerAsKept()
    {
      MockPack
          .SetupContainerList(new Container { Id = "existing-id", Name = "/reused" })
          .SetupContainerInspect("existing-id", running: true)
          .SetupContainerRemove();

      var ex = await Assert.ThrowsAsync<FluentDockerException>(() => new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c.UseImage("nginx").WithName("reused").ReuseIfExists())
          .UseContainer(c => c.WithName("invalid"))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken));

      var manifest = Assert.IsType<BuildFailureManifest>(ex.Data["BuildFailureManifest"]);
      var kept = Assert.Single(manifest.KeptResources);
      Assert.Equal("container", kept.Kind);
      Assert.Equal("borrowed", kept.Reason);
    }

    [Fact]
    public async Task FailureManifest_IncludesBuiltImageAsKept()
    {
      MockPack.ImageDriver
          .Setup(d => d.BuildAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<ImageBuildConfig>(),
              It.IsAny<IProgress<ImageBuildProgress>>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<ImageBuildResult>.Ok(
              new ImageBuildResult { ImageId = "image-id" }));

      var ex = await Assert.ThrowsAsync<FluentDockerException>(() => new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseImage("test-image", d => d.FromString("FROM scratch"))
          .UseContainer(c => c.WithName("invalid"))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken));

      var manifest = Assert.IsType<BuildFailureManifest>(ex.Data["BuildFailureManifest"]);
      var kept = Assert.Single(manifest.KeptResources);
      Assert.Equal("image", kept.Kind);
      Assert.Equal("built", kept.Reason);
    }

    [Fact]
    public async Task PodBuilder_ResetForRetryClearsAttemptState()
    {
      var podDriver = new Mock<IPodmanPodDriver>();
      podDriver
          .Setup(d => d.CreatePodAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<PodCreateConfig>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<PodCreateResult>.Ok(
              new PodCreateResult { Id = "pod-id" }));
      podDriver
          .Setup(d => d.StartPodAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));
      MockPack.RegisterCustomDriver(podDriver.Object);

      var builderType = typeof(Builder).Assembly.GetType("FluentDocker.Builders.PodBuilder");
      Assert.NotNull(builderType);
      var builder = (IPodBuilder)Activator.CreateInstance(builderType, Kernel, DriverId)!;
      builder.WithName("pod");
      var execute = builderType.GetMethod("ExecuteAsync", BindingFlags.Instance | BindingFlags.Public);
      var reset = builderType.GetMethod("ResetForRetry", BindingFlags.Instance | BindingFlags.NonPublic);
      var pending = builderType.GetProperty("PendingService", BindingFlags.Instance | BindingFlags.NonPublic);
      var created = builderType.GetProperty("CreatedResource", BindingFlags.Instance | BindingFlags.NonPublic);
      Assert.NotNull(execute);
      Assert.NotNull(reset);
      Assert.NotNull(pending);
      Assert.NotNull(created);

      var service = await (Task<IServiceAsync>)execute.Invoke(builder, [TestContext.Current.CancellationToken])!;
      Assert.NotNull(service);
      Assert.Same(service, pending.GetValue(builder));
      Assert.True((bool)created.GetValue(builder)!);

      reset.Invoke(builder, []);

      Assert.Null(pending.GetValue(builder));
      Assert.False((bool)created.GetValue(builder)!);
    }

    [Fact]
    public async Task BuildAsync_ReusedRunningContainerWithLink_IsNotStartedByDeferredPass()
    {
      // A reused, already-running container that also declares a link must NOT be (re)started by
      // the deferred link-start pass. StartDeferred is an execute-time decision, and the reuse
      // branches return before the deferred-start path is armed, so a borrowed running container
      // is never double-started.
      MockPack
          .SetupContainerList(new Container { Id = "app-existing-id", Name = "/app" })
          .SetupContainerInspect("app-existing-id", running: true)
          .SetupContainerStart();

      await using var results = await new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c
              .UseImage("nginx")
              .WithName("app")
              .ReuseIfExists()
              .WithLink("external-db"))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);

      MockPack.ContainerDriver.Verify(d => d.StartAsync(
          It.IsAny<DriverContext>(), "app-existing-id", It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task BuildAsync_SameNetworkNameInDifferentScopes_ValidatesWithoutCrossScopeFalsePositive()
    {
      // Scope A (docker) and Scope B (docker-b) each declare a network named "shared-net". A
      // container in Scope B references "shared-net"; reference validation must resolve it to
      // Scope B's own network (declared before it), NOT falsely reject it because an identically
      // named network exists in another driver scope.
      MockPack.SetupNetworkList().SetupNetworkCreate("net-a");

      var packB = new MockDriverPack();
      packB
          .SetupNetworkList()
          .SetupNetworkCreate("net-b")
          .SetupContainerCreate("web-b")
          .SetupContainerStart()
          .SetupContainerInspect("web-b", running: true);
      await Kernel.RegisterDriverPackAsync(
          "docker-b", packB, new DriverContext("docker-b"),
          TestContext.Current.CancellationToken);

      await using var results = await new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseNetwork(n => n.WithName("shared-net"))
          .WithinDriver("docker-b", Kernel)
          .UseNetwork(n => n.WithName("shared-net"))
          .UseContainer(c => c
              .UseImage("nginx")
              .WithName("web-b")
              .WithNetwork("shared-net"))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);

      Assert.Equal(2, results.Networks.Count);
      Assert.Single(results.Containers);
    }
  }
}
