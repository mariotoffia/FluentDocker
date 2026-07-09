using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Model.Containers;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Volumes;
using FluentDocker.Testing.Core;
using FluentDocker.Tests.Mocks;
using Moq;
using Xunit;
using Container = FluentDocker.Model.Containers.Container;
using DriverContext = FluentDocker.Model.Drivers.DriverContext;

namespace FluentDocker.Tests.CoreTests.Testing
{
  [Trait("Category", "Unit")]
  [Collection(TestingEnvVarsCollection.Name)]
  public class OrphanCleanupSafetyTests : MockKernelTestBase, IAsyncLifetime
  {
    public async ValueTask InitializeAsync()
    {
      await InitializeMockKernelAsync();
    }

    [Fact]
    public async Task CleanupOrphanedResources_DefaultMinimumAge_PreservesYoungResource()
    {
      var young = DateTime.UtcNow.AddMinutes(-10);
      SetupContainerList(LabeledContainer("young", "other-session", young));
      SetupContainerInspect(LabeledContainer(
          "young", "other-session", young,
          running: false, created: DateTimeOffset.UtcNow.AddMinutes(-10)));
      SetupEmptyNetworkList();
      SetupEmptyVolumeList();
      MockPack.SetupContainerRemove();

      var result = await OrphanCleanup.CleanupOrphanedResourcesAsync(
          Kernel, DriverId, "current-session",
          cancellationToken: TestContext.Current.CancellationToken);

      Assert.Equal(0, result.ContainersRemoved);
      VerifyContainerRemove("young", Times.Never());
    }

    [Fact]
    public async Task CleanupOrphanedResources_ExplicitZeroMinimumAge_RemovesYoungResource()
    {
      var young = DateTime.UtcNow.AddMinutes(-10);
      SetupContainerList(LabeledContainer("young", "other-session", young));
      SetupContainerInspect(LabeledContainer(
          "young", "other-session", young,
          running: false, created: DateTimeOffset.UtcNow.AddMinutes(-10)));
      SetupEmptyNetworkList();
      SetupEmptyVolumeList();
      MockPack.SetupContainerRemove();

      var result = await OrphanCleanup.CleanupOrphanedResourcesAsync(
          Kernel, DriverId, "current-session", TimeSpan.Zero,
          TestContext.Current.CancellationToken);

      Assert.Equal(1, result.ContainersRemoved);
      VerifyContainerRemove("young", Times.Once());
    }

    [Fact]
    public async Task CleanupOrphanedResources_OldRunningContainer_PreservesContainer()
    {
      var oldEnough = DateTime.UtcNow.AddHours(-2);
      var container = LabeledContainer("live-container", "other-session", oldEnough);
      SetupContainerList(container);
      SetupContainerInspect(LabeledContainer(
          "live-container", "other-session", oldEnough,
          running: true, created: DateTimeOffset.UtcNow.AddHours(-2)));
      SetupEmptyNetworkList();
      SetupEmptyVolumeList();
      MockPack.SetupContainerRemove();

      var result = await OrphanCleanup.CleanupOrphanedResourcesAsync(
          Kernel, DriverId, "current-session", TimeSpan.FromHours(1),
          TestContext.Current.CancellationToken);

      Assert.Equal(0, result.ContainersRemoved);
      MockPack.ContainerDriver.Verify(
          d => d.InspectAsync(
              It.IsAny<DriverContext>(), "live-container",
              It.IsAny<CancellationToken>()),
          Times.Once);
      VerifyContainerRemove("live-container", Times.Never());
    }

    [Fact]
    public async Task CleanupOrphanedResources_SharedSessionEnvironment_PreservesMatchingSession()
    {
      var previous = Environment.GetEnvironmentVariable("FLUENTDOCKER_TEST_SESSION");
      Environment.SetEnvironmentVariable("FLUENTDOCKER_TEST_SESSION", "shared-session");
      try
      {
        var oldEnough = DateTime.UtcNow.AddHours(-2);
        SetupContainerList(LabeledContainer("shared", "shared-session", oldEnough));
        SetupContainerInspect(LabeledContainer(
            "shared", "shared-session", oldEnough,
            running: false, created: DateTimeOffset.UtcNow.AddHours(-2)));
        SetupEmptyNetworkList();
        SetupEmptyVolumeList();
        MockPack.SetupContainerRemove();

        var result = await OrphanCleanup.CleanupOrphanedResourcesAsync(
            Kernel, DriverId, "current-session", TimeSpan.FromHours(1),
            TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ContainersRemoved);
        VerifyContainerRemove("shared", Times.Never());
      }
      finally
      {
        Environment.SetEnvironmentVariable("FLUENTDOCKER_TEST_SESSION", previous);
      }
    }

    [Fact]
    public async Task CleanupOrphanedResources_DaemonCreatedIsRecent_PreservesContainer()
    {
      var staleCreatorClock = DateTime.UtcNow.AddHours(-2);
      SetupContainerList(LabeledContainer("recent-daemon", "other-session", staleCreatorClock));
      SetupContainerInspect(LabeledContainer(
          "recent-daemon", "other-session", staleCreatorClock,
          running: false, created: DateTimeOffset.UtcNow));
      SetupEmptyNetworkList();
      SetupEmptyVolumeList();
      MockPack.SetupContainerRemove();

      var result = await OrphanCleanup.CleanupOrphanedResourcesAsync(
          Kernel, DriverId, "current-session", TimeSpan.FromHours(1),
          TestContext.Current.CancellationToken);

      Assert.Equal(0, result.ContainersRemoved);
      VerifyContainerRemove("recent-daemon", Times.Never());
    }

    private void SetupContainerList(params Container[] containers)
    {
      MockPack.ContainerDriver
          .Setup(d => d.ListAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<ContainerListFilter>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<IList<Container>>.Ok([.. containers]));
    }

    private void SetupContainerInspect(Container container)
    {
      MockPack.ContainerDriver
          .Setup(d => d.InspectAsync(
              It.IsAny<DriverContext>(),
              container.Id!,
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Container>.Ok(container));
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

    private void SetupEmptyVolumeList()
    {
      MockPack.VolumeDriver
          .Setup(d => d.ListAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<VolumeListFilter>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<IList<Volume>>.Ok([]));
    }

    private void VerifyContainerRemove(string id, Times times)
    {
      MockPack.ContainerDriver.Verify(
          d => d.RemoveAsync(
              It.IsAny<DriverContext>(), id, true, false,
              It.IsAny<CancellationToken>()),
          times);
    }

    private static Container LabeledContainer(
        string id,
        string sessionId,
        DateTime createdAt,
        bool running = false,
        DateTimeOffset created = default)
    {
      return new Container
      {
        Id = id,
        Created = created,
        State = new ContainerState
        {
          Running = running,
          Status = running ? "running" : "exited"
        },
        Config = new ContainerConfig
        {
          Labels = new Dictionary<string, string>
          {
            [SessionLabel.Key] = sessionId,
            [SessionLabel.ManagedKey] = "true",
            [SessionLabel.CreatedAtKey] = createdAt.ToString("o")
          }
        }
      };
    }
  }
}
