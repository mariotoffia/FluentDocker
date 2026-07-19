using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Containers;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Networks;
using FluentDocker.Model.Volumes;
using FluentDocker.Testing.Core;
using FluentDocker.Testing.Xunit;
using FluentDocker.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using Container = FluentDocker.Model.Containers.Container;
using DriverContext = FluentDocker.Model.Drivers.DriverContext;

namespace FluentDocker.Tests.CoreTests.Testing
{
  [Trait("Category", "Unit")]
  [Collection(TestingEnvVarsCollection.Name)]
  public class ProdReadyTestingFixesTests : MockKernelTestBase, IAsyncLifetime
  {
    public async ValueTask InitializeAsync()
    {
      await InitializeMockKernelAsync();
    }

    [Fact]
    public async Task OrphanCleanup_SkipsInUseNetwork()
    {
      var oldEnough = DateTime.UtcNow.AddHours(-2);
      var network = LabeledNetwork("net-in-use", "other-session", oldEnough);
      SetupEmptyContainerList();
      SetupNetworkList(network);
      SetupNetworkInspect(new Network
      {
        Id = network.Id,
        Name = network.Name,
        Containers =
        {
          ["container-id"] = new NetworkedContainer { Name = "live-container" }
        }
      });
      SetupEmptyVolumeList();
      MockPack.SetupNetworkRemove();

      var result = await OrphanCleanup.CleanupOrphanedResourcesAsync(
          Kernel, DriverId, "current-session", TimeSpan.FromHours(1),
          TestContext.Current.CancellationToken);

      Assert.Equal(0, result.NetworksRemoved);
      MockPack.NetworkDriver.Verify(
          d => d.RemoveAsync(
              It.IsAny<DriverContext>(), "net-in-use",
              It.IsAny<CancellationToken>()),
          Times.Never);
    }

    [Fact]
    public async Task OrphanCleanup_SkipsInUseVolume()
    {
      var oldEnough = DateTime.UtcNow.AddHours(-2);
      SetupManagedContainerList(new Container
      {
        Id = "live-container",
        Config = new ContainerConfig
        {
          Labels = SessionLabel.CreateLabels("current-session")
        },
        Mounts = [new ContainerMount { Name = "vol-in-use" }]
      });
      SetupEmptyNetworkList();
      SetupVolumeList(LabeledVolume("vol-in-use", "other-session", oldEnough));
      MockPack.SetupVolumeRemove();

      var result = await OrphanCleanup.CleanupOrphanedResourcesAsync(
          Kernel, DriverId, "current-session", TimeSpan.FromHours(1),
          TestContext.Current.CancellationToken);

      Assert.Equal(0, result.VolumesRemoved);
      MockPack.VolumeDriver.Verify(
          d => d.RemoveAsync(
              It.IsAny<DriverContext>(), "vol-in-use", It.IsAny<bool>(),
              It.IsAny<CancellationToken>()),
          Times.Never);
    }

    [Fact]
    public async Task OrphanCleanup_VolumeProbeFailure_SkipsRemoval()
    {
      var oldEnough = DateTime.UtcNow.AddHours(-2);
      SetupManagedContainerListFailure();
      SetupEmptyNetworkList();
      SetupVolumeList(LabeledVolume("vol-unknown", "other-session", oldEnough));
      MockPack.SetupVolumeRemove();

      var result = await OrphanCleanup.CleanupOrphanedResourcesAsync(
          Kernel, DriverId, "current-session", TimeSpan.FromHours(1),
          TestContext.Current.CancellationToken);

      Assert.Equal(0, result.VolumesRemoved);
      MockPack.VolumeDriver.Verify(
          d => d.RemoveAsync(
              It.IsAny<DriverContext>(), "vol-unknown", It.IsAny<bool>(),
              It.IsAny<CancellationToken>()),
          Times.Never);
    }

    [Fact]
    public void ProcessExitReaper_RunCleanup_DropsCollectedKernel()
    {
      var cleanupCalled = false;
      var core = new ProcessExitReaperCore(
          cleanup: (_, _, _, _) =>
          {
            cleanupCalled = true;
            return Task.CompletedTask;
          },
          isEnabled: () => true,
          sharedSessionId: () => null!);
      var weak = RegisterCollectableKernel(core);

      Assert.True(CollectUntilDead(weak));
      core.RunCleanup();

      Assert.False(cleanupCalled);
      Assert.Equal(0, core.RegistrationCount);
    }

    [Fact]
    public async Task XunitContainerFixtureBase_IsDockerAvailableAsync_ProbesOnce()
    {
      var calls = 0;
      var fixture = new AvailabilityProbeFixture(async () =>
      {
        calls++;
        var (kernel, pack) = await MockKernelBuilderExtensions
            .CreateWithMockDriverAsync("docker").ConfigureAwait(false);
        pack.SystemDriver
            .Setup(d => d.PingAsync(
                It.IsAny<DriverContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));
        return kernel;
      });

      Assert.True(await fixture.IsDockerAvailableAsync(TestContext.Current.CancellationToken));
      Assert.True(await fixture.IsDockerAvailableAsync(TestContext.Current.CancellationToken));
      Assert.Equal(1, calls);
    }

    [Fact]
    public void ProcessExitReaperCore_SharedRegistrationSurvivesFailedSiblingCleanup()
    {
      var core = new ProcessExitReaperCore(
          cleanup: (_, _, _, _) => Task.CompletedTask,
          isEnabled: () => true,
          sharedSessionId: () => null!);
      var options = new DockerResourceOptions { SessionId = "owned-session" };

      Assert.True(core.Register(Kernel, DriverId, options));
      Assert.True(core.Register(Kernel, DriverId, options));
      Assert.Equal(1, core.RegistrationCount);

      core.Unregister(Kernel, DriverId, options.SessionId);
      Assert.Equal(1, core.RegistrationCount);

      core.Unregister(Kernel, DriverId, options.SessionId);
      Assert.Equal(0, core.RegistrationCount);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference RegisterCollectableKernel(
        ProcessExitReaperCore core)
    {
      var kernel = new FluentDockerKernel(
          new DriverRegistry(NullLoggerFactory.Instance),
          NullLoggerFactory.Instance);
      Assert.True(core.Register(
          kernel, "docker",
          new DockerResourceOptions { SessionId = "collectable-session" }));
      var weak = new WeakReference(kernel);
      kernel = null!;
      return weak;
    }

    private static bool CollectUntilDead(WeakReference weak)
    {
      for (var i = 0; i < 10; i++)
      {
        ForceFullCollection();
        if (!weak.IsAlive)
          return true;
      }

      return false;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ForceFullCollection()
    {
      GC.Collect();
      GC.WaitForPendingFinalizers();
      GC.Collect();
    }

    private void SetupEmptyContainerList()
    {
      MockPack.ContainerDriver
          .Setup(d => d.ListAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<ContainerListFilter>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<IList<Container>>.Ok([]));
    }

    private void SetupManagedContainerList(params Container[] containers)
    {
      MockPack.ContainerDriver
          .Setup(d => d.ListAsync(
              It.IsAny<DriverContext>(),
              It.Is<ContainerListFilter>(f => f.Labels.ContainsKey(SessionLabel.ManagedKey)),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<IList<Container>>.Ok([.. containers]));
    }

    private void SetupManagedContainerListFailure()
    {
      MockPack.ContainerDriver
          .Setup(d => d.ListAsync(
              It.IsAny<DriverContext>(),
              It.Is<ContainerListFilter>(f => f.Labels.ContainsKey(SessionLabel.ManagedKey)),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<IList<Container>>.Fail("list failed"));
    }

    private void SetupEmptyNetworkList()
    {
      MockPack.NetworkDriver
          .Setup(d => d.ListAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<NetworkListFilter>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<IList<Network>>.Ok([]));
    }

    private void SetupNetworkList(params Network[] networks)
    {
      MockPack.NetworkDriver
          .Setup(d => d.ListAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<NetworkListFilter>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<IList<Network>>.Ok([.. networks]));
    }

    private void SetupNetworkInspect(Network network)
    {
      MockPack.NetworkDriver
          .Setup(d => d.InspectAsync(
              It.IsAny<DriverContext>(),
              network.Id,
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Network>.Ok(network));
    }

    private void SetupEmptyVolumeList()
    {
      MockPack.VolumeDriver
          .Setup(d => d.ListAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<VolumeListFilter>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<IList<Volume>>.Ok([]));
    }

    private void SetupVolumeList(params Volume[] volumes)
    {
      MockPack.VolumeDriver
          .Setup(d => d.ListAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<VolumeListFilter>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<IList<Volume>>.Ok([.. volumes]));
    }

    private static Network LabeledNetwork(
        string id,
        string sessionId,
        DateTime createdAt)
    {
      return new Network
      {
        Id = id,
        Name = id,
        Labels =
        {
          [SessionLabel.Key] = sessionId,
          [SessionLabel.ManagedKey] = "true",
          [SessionLabel.CreatedAtKey] = createdAt.ToString("o")
        }
      };
    }

    private static Volume LabeledVolume(
        string name,
        string sessionId,
        DateTime createdAt)
    {
      return new Volume
      {
        Name = name,
        Labels = new Dictionary<string, string>
        {
          [SessionLabel.Key] = sessionId,
          [SessionLabel.ManagedKey] = "true",
          [SessionLabel.CreatedAtKey] = createdAt.ToString("o")
        }
      };
    }

    private sealed class AvailabilityProbeFixture(
        Func<Task<FluentDockerKernel>> kernelFactory) : XunitContainerFixtureBase
    {
      protected override Func<Task<FluentDockerKernel>>? KernelFactory => kernelFactory;

      protected override DockerResourceOptions? GetOptions() =>
          new() { Driver = DriverSelection.Specific("docker") };

      protected override void ConfigureContainer(IContainerBuilder builder) { }
    }

  }
}
