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
  public class OrphanCleanupTests : MockKernelTestBase, IAsyncLifetime
  {
    public async ValueTask InitializeAsync()
    {
      await InitializeMockKernelAsync();
    }

    [Fact]
    public async Task CleanupOrphanedResources_RemovesOldSessionContainers()
    {
      var currentSession = "current-session-id";
      var orphanSession = "old-session-id";

      MockPack.ContainerDriver
          .Setup(d => d.ListAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<ContainerListFilter>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(FluentDocker.Model.Drivers.CommandResponse<IList<Container>>.Ok(
              new List<Container>
              {
                new Container
                {
                  Id = "orphan-1",
                  Config = new ContainerConfig
                  {
                    Labels = new Dictionary<string, string>
                    {
                      [SessionLabel.Key] = orphanSession,
                      [SessionLabel.ManagedKey] = "true"
                    }
                  }
                },
                new Container
                {
                  Id = "current-1",
                  Config = new ContainerConfig
                  {
                    Labels = new Dictionary<string, string>
                    {
                      [SessionLabel.Key] = currentSession,
                      [SessionLabel.ManagedKey] = "true"
                    }
                  }
                }
              }));

      SetupRemoveDrivers();

      var result = await OrphanCleanup.CleanupOrphanedResourcesAsync(
          Kernel, DriverId, currentSession, TestContext.Current.CancellationToken);

      Assert.Equal(1, result.ContainersRemoved);
      MockPack.ContainerDriver.Verify(
          d => d.RemoveAsync(
              It.IsAny<DriverContext>(), "orphan-1", true, false,
              It.IsAny<CancellationToken>()),
          Times.Once);
      MockPack.ContainerDriver.Verify(
          d => d.RemoveAsync(
              It.IsAny<DriverContext>(), "current-1", It.IsAny<bool>(),
              It.IsAny<bool>(), It.IsAny<CancellationToken>()),
          Times.Never);
    }

    [Fact]
    public async Task CleanupOrphanedResources_RemovesOldSessionNetworks()
    {
      var currentSession = "current";
      var orphanSession = "orphan";

      SetupEmptyContainerList();
      SetupEmptyVolumeList();

      MockPack.NetworkDriver
          .Setup(d => d.ListAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<NetworkListFilter>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(FluentDocker.Model.Drivers.CommandResponse<IList<Network>>.Ok(
              new List<Network>
              {
                new Network
                {
                  Id = "net-orphan",
                  Name = "orphan-net",
                  Labels = new Dictionary<string, string>
                  {
                    [SessionLabel.Key] = orphanSession,
                    [SessionLabel.ManagedKey] = "true"
                  }
                }
              }));

      MockPack.SetupNetworkRemove();

      var result = await OrphanCleanup.CleanupOrphanedResourcesAsync(
          Kernel, DriverId, currentSession, TestContext.Current.CancellationToken);

      Assert.Equal(1, result.NetworksRemoved);
    }

    [Fact]
    public async Task CleanupOrphanedResources_RemovesOldSessionVolumes()
    {
      var currentSession = "current";
      var orphanSession = "orphan";

      SetupEmptyContainerList();
      SetupEmptyNetworkList();

      MockPack.VolumeDriver
          .Setup(d => d.ListAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<VolumeListFilter>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(FluentDocker.Model.Drivers.CommandResponse<IList<Volume>>.Ok(
              new List<Volume>
              {
                new Volume
                {
                  Name = "vol-orphan",
                  Labels = new Dictionary<string, string>
                  {
                    [SessionLabel.Key] = orphanSession,
                    [SessionLabel.ManagedKey] = "true"
                  }
                }
              }));

      MockPack.SetupVolumeRemove();

      var result = await OrphanCleanup.CleanupOrphanedResourcesAsync(
          Kernel, DriverId, currentSession, TestContext.Current.CancellationToken);

      Assert.Equal(1, result.VolumesRemoved);
    }

    [Fact]
    public async Task CleanupOrphanedResources_NullSession_RemovesAll()
    {
      MockPack.ContainerDriver
          .Setup(d => d.ListAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<ContainerListFilter>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(FluentDocker.Model.Drivers.CommandResponse<IList<Container>>.Ok(
              new List<Container>
              {
                new Container
                {
                  Id = "any-container",
                  Config = new ContainerConfig
                  {
                    Labels = new Dictionary<string, string>
                    {
                      [SessionLabel.Key] = "some-session",
                      [SessionLabel.ManagedKey] = "true"
                    }
                  }
                }
              }));

      SetupRemoveDrivers();

      var result = await OrphanCleanup.CleanupOrphanedResourcesAsync(
          Kernel, DriverId, currentSessionId: null,
          TestContext.Current.CancellationToken);

      Assert.Equal(1, result.ContainersRemoved);
    }

    [Fact]
    public async Task CleanupOrphanedResources_NoOrphans_ReturnsZero()
    {
      var session = "my-session";

      MockPack.ContainerDriver
          .Setup(d => d.ListAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<ContainerListFilter>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(FluentDocker.Model.Drivers.CommandResponse<IList<Container>>.Ok(
              new List<Container>
              {
                new Container
                {
                  Id = "mine",
                  Config = new ContainerConfig
                  {
                    Labels = new Dictionary<string, string>
                    {
                      [SessionLabel.Key] = session,
                      [SessionLabel.ManagedKey] = "true"
                    }
                  }
                }
              }));

      SetupEmptyNetworkList();
      SetupEmptyVolumeList();

      var result = await OrphanCleanup.CleanupOrphanedResourcesAsync(
          Kernel, DriverId, session, TestContext.Current.CancellationToken);

      Assert.Equal(0, result.TotalRemoved);
    }

    [Fact]
    public void SessionLabel_CreateLabels_IncludesAllKeys()
    {
      var labels = SessionLabel.CreateLabels("test-session");

      Assert.Equal("test-session", labels[SessionLabel.Key]);
      Assert.Equal("true", labels[SessionLabel.ManagedKey]);
      Assert.True(labels.ContainsKey(SessionLabel.CreatedAtKey));
    }

    [Fact]
    public void SessionLabel_NewSessionId_GeneratesUniqueIds()
    {
      var id1 = SessionLabel.NewSessionId();
      var id2 = SessionLabel.NewSessionId();

      Assert.NotEqual(id1, id2);
      Assert.Equal(32, id1.Length); // GUID without hyphens
    }

    #region Helpers

    private void SetupRemoveDrivers()
    {
      MockPack.SetupContainerRemove();
      MockPack.SetupNetworkRemove();
      MockPack.SetupVolumeRemove();
      SetupEmptyNetworkList();
      SetupEmptyVolumeList();
    }

    private void SetupEmptyContainerList()
    {
      MockPack.ContainerDriver
          .Setup(d => d.ListAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<ContainerListFilter>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(FluentDocker.Model.Drivers.CommandResponse<IList<Container>>.Ok(
              new List<Container>()));
    }

    private void SetupEmptyNetworkList()
    {
      MockPack.NetworkDriver
          .Setup(d => d.ListAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<NetworkListFilter>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(FluentDocker.Model.Drivers.CommandResponse<IList<Network>>.Ok(
              new List<Network>()));
    }

    private void SetupEmptyVolumeList()
    {
      MockPack.VolumeDriver
          .Setup(d => d.ListAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<VolumeListFilter>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(FluentDocker.Model.Drivers.CommandResponse<IList<Volume>>.Ok(
              new List<Volume>()));
    }

    #endregion
  }
}
