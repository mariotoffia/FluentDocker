using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Model.Containers;
using FluentDocker.Model.Drivers;
using FluentDocker.Services.Impl;
using FluentDocker.Tests.Mocks;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Service
{
  /// <summary>
  /// Named-volume dispose-cleanup partial of <see cref="ContainerServiceProductionReadinessTests"/>.
  /// </summary>
  public partial class ContainerServiceProductionReadinessTests
  {
    // S-M2 counterpart of DisposeAsync_NamedVolumeCleanupThrowsOperationCanceled_StopsLoop...:
    // only OCE stops the loop. A NON-cancellation failure on one volume (driver bug, transient
    // daemon error) must be swallowed-and-logged and the remaining volumes still attempted —
    // one bad volume must not leak every volume after it.
    [Fact]
    public async Task DisposeAsync_NamedVolumeCleanupThrowsNonCancellation_ContinuesWithRemainingVolumes()
    {
      MockPack.SetupContainerRemove();
      MockPack.ContainerDriver
          .Setup(d => d.InspectAsync(
              It.IsAny<DriverContext>(), "container-123", It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Container>.Ok(new Container
          {
            Id = "container-123",
            Mounts =
            [
              new ContainerMount { Name = "orders-data" },
              new ContainerMount { Name = "orders-log" },
              new ContainerMount { Name = "orders-cache" }
            ]
          }));
      MockPack.VolumeDriver
          .SetupSequence(d => d.RemoveAsync(
              It.IsAny<DriverContext>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
          .ThrowsAsync(new InvalidOperationException("driver invariant violated"))
          .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default))
          .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));
      var service = new ContainerService(
          Kernel, DriverId, "container-123", "alpine", "test",
          stopOnDispose: false, deleteOnDispose: true, deleteNamedVolumeOnDispose: true);

      await service.DisposeAsync();

      // All three volumes attempted — the first throw did not abort the loop...
      MockPack.VolumeDriver.Verify(d => d.RemoveAsync(
          It.IsAny<DriverContext>(), It.IsAny<string>(), It.IsAny<bool>(),
          It.IsAny<CancellationToken>()), Times.Exactly(3));
      // ...and the volumes after the failed one were the ones that got removed.
      MockPack.VolumeDriver.Verify(d => d.RemoveAsync(
          It.IsAny<DriverContext>(), "orders-log", It.IsAny<bool>(),
          It.IsAny<CancellationToken>()), Times.Once);
      MockPack.VolumeDriver.Verify(d => d.RemoveAsync(
          It.IsAny<DriverContext>(), "orders-cache", It.IsAny<bool>(),
          It.IsAny<CancellationToken>()), Times.Once);
    }
  }
}
