using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Model.Containers;
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
  public class OrphanCleanupCliLabelRefreshTests : MockKernelTestBase, IAsyncLifetime
  {
    public async ValueTask InitializeAsync()
    {
      await InitializeMockKernelAsync();
    }

    [Fact]
    public async Task CleanupOrphanedResources_NullListLabelsAndInspectCurrentSession_PreservesContainer()
    {
      var currentSession = "current-session";
      var oldEnough = DateTime.UtcNow.AddHours(-2);

      SetupContainerList(new Container { Id = "current-1" });
      SetupContainerInspect("current-1", currentSession, oldEnough);
      SetupEmptyNetworkList();
      SetupEmptyVolumeList();
      MockPack.SetupContainerRemove();

      var result = await OrphanCleanup.CleanupOrphanedResourcesAsync(
          Kernel, DriverId, currentSession, TimeSpan.FromHours(1),
          TestContext.Current.CancellationToken);

      Assert.Equal(0, result.ContainersRemoved);
      VerifyContainerInspect("current-1", Times.Once());
      VerifyContainerRemove("current-1", Times.Never());
    }

    [Fact]
    public async Task CleanupOrphanedResources_NullListLabelsAndInspectOldOrphan_RemovesContainer()
    {
      var currentSession = "current-session";
      var oldEnough = DateTime.UtcNow.AddHours(-2);

      SetupContainerList(new Container { Id = "orphan-1" });
      SetupContainerInspect("orphan-1", "other-session", oldEnough);
      SetupEmptyNetworkList();
      SetupEmptyVolumeList();
      MockPack.SetupContainerRemove();

      var result = await OrphanCleanup.CleanupOrphanedResourcesAsync(
          Kernel, DriverId, currentSession, TimeSpan.FromHours(1),
          TestContext.Current.CancellationToken);

      Assert.Equal(1, result.ContainersRemoved);
      VerifyContainerInspect("orphan-1", Times.Once());
      VerifyContainerRemove("orphan-1", Times.Once());
    }

    [Fact]
    public async Task CleanupOrphanedResources_MinimumAgeZeroAndNullListLabelsAndInspectCurrentSession_PreservesContainer()
    {
      var currentSession = "current-session";
      var oldEnough = DateTime.UtcNow.AddHours(-2);

      SetupContainerList(new Container { Id = "current-1" });
      SetupContainerInspect("current-1", currentSession, oldEnough);
      SetupEmptyNetworkList();
      SetupEmptyVolumeList();
      MockPack.SetupContainerRemove();

      var result = await OrphanCleanup.CleanupOrphanedResourcesAsync(
          Kernel, DriverId, currentSession, TimeSpan.Zero,
          TestContext.Current.CancellationToken);

      Assert.Equal(0, result.ContainersRemoved);
      VerifyContainerInspect("current-1", Times.Once());
      VerifyContainerRemove("current-1", Times.Never());
    }

    [Fact]
    public async Task CleanupOrphanedResources_ListLabelsCurrentSession_PreservesContainerWithoutInspect()
    {
      var currentSession = "current-session";
      var oldEnough = DateTime.UtcNow.AddHours(-2);

      SetupContainerList(LabeledContainer("current-1", currentSession, oldEnough));
      SetupEmptyNetworkList();
      SetupEmptyVolumeList();
      MockPack.SetupContainerRemove();

      var result = await OrphanCleanup.CleanupOrphanedResourcesAsync(
          Kernel, DriverId, currentSession, TimeSpan.Zero,
          TestContext.Current.CancellationToken);

      Assert.Equal(0, result.ContainersRemoved);
      VerifyContainerInspect("current-1", Times.Never());
      VerifyContainerRemove("current-1", Times.Never());
    }

    private void SetupContainerList(params Container[] containers)
    {
      MockPack.ContainerDriver
          .Setup(d => d.ListAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<ContainerListFilter>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(FluentDocker.Model.Drivers.CommandResponse<IList<Container>>.Ok(
              [.. containers]));
    }

    private void SetupContainerInspect(string id, string sessionId, DateTime createdAt)
    {
      MockPack.ContainerDriver
          .Setup(d => d.InspectAsync(
              It.IsAny<DriverContext>(),
              id,
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(FluentDocker.Model.Drivers.CommandResponse<Container>.Ok(
              LabeledContainer(id, sessionId, createdAt)));
    }

    private void SetupEmptyNetworkList()
    {
      MockPack.NetworkDriver
          .Setup(d => d.ListAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<NetworkListFilter>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(FluentDocker.Model.Drivers.CommandResponse<IList<Network>>.Ok(
              []));
    }

    private void SetupEmptyVolumeList()
    {
      MockPack.VolumeDriver
          .Setup(d => d.ListAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<VolumeListFilter>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(FluentDocker.Model.Drivers.CommandResponse<IList<Volume>>.Ok(
              []));
    }

    private void VerifyContainerInspect(string id, Times times)
    {
      MockPack.ContainerDriver.Verify(
          d => d.InspectAsync(
              It.IsAny<DriverContext>(), id, It.IsAny<CancellationToken>()),
          times);
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
        DateTime createdAt)
    {
      return new Container
      {
        Id = id,
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
