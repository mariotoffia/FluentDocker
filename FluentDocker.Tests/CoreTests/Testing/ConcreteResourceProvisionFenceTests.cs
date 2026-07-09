using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Podman;
using FluentDocker.Model.Containers;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Models;
using FluentDocker.Services;
using FluentDocker.Testing.Core;
using FluentDocker.Tests.Mocks;
using Moq;
using Xunit;
using Container = FluentDocker.Model.Containers.Container;
using DriverContext = FluentDocker.Model.Drivers.DriverContext;

namespace FluentDocker.Tests.CoreTests.Testing
{
  [Trait("Category", "Unit")]
  public partial class ConcreteResourceProvisionFenceTests : MockKernelTestBase, IAsyncLifetime
  {
    public async ValueTask InitializeAsync()
    {
      await InitializeMockKernelAsync();
    }

    [Fact]
    public async Task ContainerResource_LateProvision_DoesNotOverwriteFreshContainer()
    {
      var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      var removed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      var startCalls = 0;
      SetupContainerIds();
      MockPack.ContainerDriver
          .Setup(d => d.StartAsync(It.IsAny<DriverContext>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
          .Returns(async () =>
          {
            if (Interlocked.Increment(ref startCalls) == 1)
            {
              entered.SetResult();
              await release.Task.ConfigureAwait(false);
            }
            return CommandResponse<Unit>.Ok(Unit.Default);
          });
      SetupContainerRemove(removed, "stale-container");
      var resource = new ContainerResource(Kernel, c => c.UseImage("alpine"), Options());

      await AbandonFirstProvisionAsync(resource, entered.Task);
      await resource.InitializeAsync(TestContext.Current.CancellationToken);
      Assert.Equal("fresh-container", resource.Container.Id);

      release.SetResult();
      await removed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
      Assert.Equal("fresh-container", resource.Container.Id);
      await resource.DisposeAsync();
    }

    [Fact]
    public async Task ComposeResource_LateProvision_DoesNotOverwriteFreshService()
    {
      var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      var removed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      var upCalls = 0;
      MockPack.SetupComposeList();
      MockPack.ComposeDriver
          .Setup(d => d.UpAsync(It.IsAny<DriverContext>(), It.IsAny<ComposeUpConfig>(), It.IsAny<CancellationToken>()))
          .Returns(async () =>
          {
            if (Interlocked.Increment(ref upCalls) == 1)
            {
              entered.SetResult();
              await release.Task.ConfigureAwait(false);
              return CommandResponse<ComposeUpResult>.Ok(new ComposeUpResult { ProjectName = "stale-project" });
            }
            return CommandResponse<ComposeUpResult>.Ok(new ComposeUpResult { ProjectName = "fresh-project" });
          });
      MockPack.ComposeDriver
          .Setup(d => d.DownAsync(It.IsAny<DriverContext>(), It.IsAny<ComposeDownConfig>(), It.IsAny<CancellationToken>()))
          .Callback<DriverContext, ComposeDownConfig, CancellationToken>((_, config, _) =>
          {
            if (config.ProjectName == "stale-project")
              removed.TrySetResult();
          })
          .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));
      var resource = new ComposeResource(
          Kernel,
          b => b.WithComposeFile("compose.yml"),
          Options());

      await AbandonFirstProvisionAsync(resource, entered.Task);
      await resource.InitializeAsync(TestContext.Current.CancellationToken);
      Assert.Equal("fresh-project", resource.Service.ProjectName);

      release.SetResult();
      await removed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
      Assert.Equal("fresh-project", resource.Service.ProjectName);
      await resource.DisposeAsync();
    }

    [Fact]
    public async Task ComposeResource_AbandonedFixedProject_DisposeCleansLateService()
    {
      var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      var removed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      MockPack.SetupComposeList();
      MockPack.ComposeDriver
          .Setup(d => d.UpAsync(It.IsAny<DriverContext>(), It.IsAny<ComposeUpConfig>(), It.IsAny<CancellationToken>()))
          .Returns(async () =>
          {
            entered.SetResult();
            await release.Task.ConfigureAwait(false);
            return CommandResponse<ComposeUpResult>.Ok(new ComposeUpResult { ProjectName = "fixed-project" });
          });
      MockPack.ComposeDriver
          .Setup(d => d.DownAsync(It.IsAny<DriverContext>(), It.IsAny<ComposeDownConfig>(), It.IsAny<CancellationToken>()))
          .Callback<DriverContext, ComposeDownConfig, CancellationToken>((_, config, _) =>
          {
            if (config.ProjectName == "fixed-project")
              removed.TrySetResult();
          })
          .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));
      var resource = new ComposeResource(
          Kernel,
          b => b.WithProjectName("fixed-project").WithComposeFile("compose.yml"),
          Options());

      await AbandonFirstProvisionAsync(resource, entered.Task);
      release.SetResult();

      await removed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task TopologyResource_LateProvision_DoesNotMutateFreshSnapshot()
    {
      var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      var removed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      var createCalls = 0;
      MockPack.ContainerDriver
          .Setup(d => d.CreateAsync(It.IsAny<DriverContext>(), It.IsAny<ContainerCreateConfig>(), It.IsAny<CancellationToken>()))
          .Returns(async () =>
          {
            if (Interlocked.Increment(ref createCalls) == 1)
            {
              entered.SetResult();
              await release.Task.ConfigureAwait(false);
              return CommandResponse<ContainerCreateResult>.Ok(new ContainerCreateResult { Id = "stale-topology" });
            }
            return CommandResponse<ContainerCreateResult>.Ok(new ContainerCreateResult { Id = "fresh-topology" });
          });
      MockPack.SetupContainerStart();
      SetupContainerInspectAny();
      SetupContainerRemove(removed, "stale-topology");
      var resource = new TopologyResource(
          Kernel,
          b => b.UseContainer(c => c.UseImage("alpine")),
          Options());

      await AbandonFirstProvisionAsync(resource, entered.Task);
      await resource.InitializeAsync(TestContext.Current.CancellationToken);
      Assert.Single(resource.Services);

      release.SetResult();
      await removed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
      Assert.Single(resource.Services);
      await resource.DisposeAsync();
    }

    [Fact]
    public async Task NetworkResource_LateProvision_DoesNotOverwriteFreshNetwork()
    {
      var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      var removed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      var createCalls = 0;
      MockPack.NetworkDriver
          .Setup(d => d.CreateAsync(It.IsAny<DriverContext>(), It.IsAny<NetworkCreateConfig>(), It.IsAny<CancellationToken>()))
          .Returns(async () =>
          {
            if (Interlocked.Increment(ref createCalls) == 1)
            {
              entered.SetResult();
              await release.Task.ConfigureAwait(false);
              return CommandResponse<NetworkCreateResult>.Ok(new NetworkCreateResult { Id = "stale-network" });
            }
            return CommandResponse<NetworkCreateResult>.Ok(new NetworkCreateResult { Id = "fresh-network" });
          });
      SetupNetworkRemove(removed, "stale-network");
      var resource = new NetworkResource(Kernel, _ => { }, Options());

      await AbandonFirstProvisionAsync(resource, entered.Task);
      await resource.InitializeAsync(TestContext.Current.CancellationToken);
      Assert.Equal("fresh-network", resource.NetworkId);

      release.SetResult();
      await removed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
      Assert.Equal("fresh-network", resource.NetworkId);
      await resource.DisposeAsync();
    }

    [Fact]
    public async Task VolumeResource_LateProvision_RemovesStaleVolume()
    {
      var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      var removed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      var createCalls = 0;
      var staleVolumeName = string.Empty;
      MockPack.VolumeDriver
          .Setup(d => d.CreateAsync(It.IsAny<DriverContext>(), It.IsAny<VolumeCreateConfig>(), It.IsAny<CancellationToken>()))
          .Returns(async (DriverContext _, VolumeCreateConfig config, CancellationToken _) =>
          {
            if (Interlocked.Increment(ref createCalls) == 1)
            {
              staleVolumeName = config.Name;
              entered.SetResult();
              await release.Task.ConfigureAwait(false);
              return CommandResponse<VolumeCreateResult>.Ok(new VolumeCreateResult { Name = config.Name });
            }
            return CommandResponse<VolumeCreateResult>.Ok(new VolumeCreateResult { Name = config.Name });
          });
      SetupVolumeRemove(removed, () => staleVolumeName);
      var resource = new VolumeResource(Kernel, _ => { }, Options());

      await AbandonFirstProvisionAsync(resource, entered.Task);
      await resource.InitializeAsync(TestContext.Current.CancellationToken);
      Assert.NotEqual(staleVolumeName, resource.VolumeName);

      release.SetResult();
      await removed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
      Assert.NotEqual(staleVolumeName, resource.VolumeName);
      await resource.DisposeAsync();
    }

    [Fact]
    public async Task ImageResource_LateProvision_DoesNotOverwriteFreshImage()
    {
      var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      var removed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      var pullCalls = 0;
      var inspectCalls = 0;
      MockPack.ImageDriver
          .Setup(d => d.PullAsync(
              It.IsAny<DriverContext>(), "alpine", "latest", null!, It.IsAny<CancellationToken>()))
          .Returns(async () =>
          {
            if (Interlocked.Increment(ref pullCalls) == 1)
            {
              entered.SetResult();
              await release.Task.ConfigureAwait(false);
            }
            return CommandResponse<Unit>.Ok(Unit.Default);
          });
      MockPack.ImageDriver
          .Setup(d => d.InspectAsync(It.IsAny<DriverContext>(), "alpine:latest", It.IsAny<CancellationToken>()))
          .ReturnsAsync(() => CommandResponse<Image>.Ok(new Image
          {
            Id = Interlocked.Increment(ref inspectCalls) == 1 ? "stale-image" : "fresh-image"
          }));
      MockPack.ImageDriver
          .Setup(d => d.RemoveAsync(It.IsAny<DriverContext>(), It.IsAny<string>(), true, false, It.IsAny<CancellationToken>()))
          .Callback<DriverContext, string, bool, bool, CancellationToken>((_, id, _, _, _) =>
          {
            if (id == "stale-image")
              removed.TrySetResult();
          })
          .ReturnsAsync(CommandResponse<ImageRemoveResult>.Ok(new ImageRemoveResult()));
      var resource = new ImageResource(Kernel, "alpine", removeOnDispose: true, options: Options());

      await AbandonFirstProvisionAsync(resource, entered.Task, dispose: false);
      release.SetResult();
      await removed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
      await resource.DisposeAsync();
      await resource.InitializeAsync(TestContext.Current.CancellationToken);
      Assert.Equal("fresh-image", resource.ImageId);
      await resource.DisposeAsync();
    }

    [Fact]
    public async Task ModelResource_CanceledProvision_DoesNotPublishService()
    {
      var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      var removed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      MockPack.EnableModelDrivers();
      MockPack.ModelRuntimeDriver
          .Setup(d => d.LoadAsync(It.IsAny<DriverContext>(), It.IsAny<ModelReference>(),
              It.IsAny<FluentDocker.Model.Models.Options.ModelRunOptions>(), It.IsAny<CancellationToken>()))
          .Returns(async () =>
          {
            entered.SetResult();
            await release.Task.ConfigureAwait(false);
            return CommandResponse<Unit>.Ok(Unit.Default);
          });
      MockPack.ModelRuntimeDriver
          .Setup(d => d.UnloadAsync(It.IsAny<DriverContext>(), It.IsAny<ModelReference>(), It.IsAny<CancellationToken>()))
          .Callback(() => removed.TrySetResult())
          .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));
      var resource = new ModelResource(Kernel, "ai/smollm2:latest", options: Options());

      await AbandonFirstProvisionAsync(resource, entered.Task, dispose: false);
      release.SetResult();
      await removed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
      Assert.Throws<InvalidOperationException>(() => _ = resource.Service);
      await resource.DisposeAsync();
    }

    [Fact]
    public async Task SwarmStackResource_LateProvision_DoesNotOverwriteFreshResult()
    {
      var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      var removed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      var deployCalls = 0;
      MockPack.SetCapabilities(new DriverCapabilities { SupportsContainers = true, SupportsStacks = true });
      MockPack.EnableStackDriver();
      MockPack.StackDriver
          .Setup(d => d.DeployAsync(It.IsAny<DriverContext>(), It.IsAny<StackDeployConfig>(), It.IsAny<CancellationToken>()))
          .Returns(async () =>
          {
            if (Interlocked.Increment(ref deployCalls) == 1)
            {
              entered.SetResult();
              await release.Task.ConfigureAwait(false);
              return CommandResponse<StackDeployResult>.Ok(new StackDeployResult { StackName = "stale-stack" });
            }
            return CommandResponse<StackDeployResult>.Ok(new StackDeployResult { StackName = "fresh-stack" });
          });
      MockPack.StackDriver
          .Setup(d => d.RemoveAsync(It.IsAny<DriverContext>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
          .Callback<DriverContext, string[], CancellationToken>((_, names, _) =>
          {
            if (Array.IndexOf(names, "stack") >= 0)
              removed.TrySetResult();
          })
          .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));
      var resource = new SwarmStackResource(
          Kernel,
          new StackDeployConfig { StackName = "stack" },
          Options());

      await AbandonFirstProvisionAsync(resource, entered.Task, dispose: false);
      release.SetResult();
      await removed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
      await resource.DisposeAsync();
      await resource.InitializeAsync(TestContext.Current.CancellationToken);
      Assert.Equal("fresh-stack", resource.DeployResult.StackName);
      await resource.DisposeAsync();
    }

    [Fact]
    public async Task PodmanKubernetesResource_LateProvision_DoesNotOverwriteFreshResult()
    {
      var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      var removed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      var playCalls = 0;
      MockPack.SetCapabilities(new DriverCapabilities { SupportsContainers = true, SupportsKubernetes = true });
      MockPack.EnablePodmanKubernetesDriver();
      MockPack.PodmanKubernetesDriver
          .Setup(d => d.PlayAsync(It.IsAny<DriverContext>(), It.IsAny<KubePlayConfig>(), It.IsAny<CancellationToken>()))
          .Returns(async () =>
          {
            if (Interlocked.Increment(ref playCalls) == 1)
            {
              entered.SetResult();
              await release.Task.ConfigureAwait(false);
              return CommandResponse<KubePlayResult>.Ok(KubeResult("stale-pod"));
            }
            return CommandResponse<KubePlayResult>.Ok(KubeResult("fresh-pod"));
          });
      MockPack.PodmanKubernetesDriver
          .Setup(d => d.DownAsync(It.IsAny<DriverContext>(), "kube.yaml", It.IsAny<CancellationToken>()))
          .Callback(() => removed.TrySetResult())
          .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));
      var resource = new PodmanKubernetesResource(
          Kernel,
          new KubePlayConfig { YamlPath = "kube.yaml" },
          Options());

      await AbandonFirstProvisionAsync(resource, entered.Task, dispose: false);
      release.SetResult();
      await removed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
      await resource.DisposeAsync();
      await resource.InitializeAsync(TestContext.Current.CancellationToken);
      Assert.Equal("fresh-pod", resource.PlayResult.Pods[0].Id);
      await resource.DisposeAsync();
    }

    private static DockerResourceOptions Options() => new()
    {
      CleanupOrphansOnInit = false,
      EnableSessionLabels = false,
      SessionId = "fence-session",
      TeardownTimeout = TimeSpan.FromMilliseconds(200)
    };

    private static async Task AbandonFirstProvisionAsync(
        ResourceBase resource,
        Task entered,
        bool dispose = true)
    {
      using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
      var init = resource.InitializeAsync(cts.Token);
      await entered.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
      await cts.CancelAsync();
      await Assert.ThrowsAnyAsync<OperationCanceledException>(() => init);
      if (dispose)
        await resource.DisposeAsync();
    }

    private void SetupContainerIds()
    {
      var createCalls = 0;
      MockPack.ContainerDriver
          .Setup(d => d.CreateAsync(It.IsAny<DriverContext>(), It.IsAny<ContainerCreateConfig>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(() =>
          {
            var id = Interlocked.Increment(ref createCalls) == 1 ? "stale-container" : "fresh-container";
            return CommandResponse<ContainerCreateResult>.Ok(new ContainerCreateResult { Id = id });
          });
      SetupContainerInspectAny();
    }

    private void SetupContainerInspectAny()
    {
      MockPack.ContainerDriver
          .Setup(d => d.InspectAsync(It.IsAny<DriverContext>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync((DriverContext _, string id, CancellationToken _) =>
              CommandResponse<Container>.Ok(new Container
              {
                Id = id,
                Name = id,
                State = new ContainerState { Running = true, Status = "running" }
              }));
    }

    private void SetupContainerRemove(TaskCompletionSource removed, string staleId)
    {
      MockPack.ContainerDriver
          .Setup(d => d.RemoveAsync(It.IsAny<DriverContext>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
          .Callback<DriverContext, string, bool, bool, CancellationToken>((_, id, _, _, _) =>
          {
            if (id == staleId)
              removed.TrySetResult();
          })
          .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));
    }

    private void SetupNetworkRemove(TaskCompletionSource removed, string staleId)
    {
      MockPack.NetworkDriver
          .Setup(d => d.RemoveAsync(It.IsAny<DriverContext>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
          .Callback<DriverContext, string, CancellationToken>((_, id, _) =>
          {
            if (id == staleId)
              removed.TrySetResult();
          })
          .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));
    }

    private void SetupVolumeRemove(TaskCompletionSource removed, Func<string> staleName)
    {
      MockPack.VolumeDriver
          .Setup(d => d.RemoveAsync(It.IsAny<DriverContext>(), It.IsAny<string>(), true, It.IsAny<CancellationToken>()))
          .Callback<DriverContext, string, bool, CancellationToken>((_, name, _, _) =>
          {
            if (name == staleName())
              removed.TrySetResult();
          })
          .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));
    }

    private static KubePlayResult KubeResult(string podId) => new()
    {
      Pods =
      [
        new KubePlayPodResult
        {
          Id = podId,
          Containers = ["container"]
        }
      ]
    };
  }
}
