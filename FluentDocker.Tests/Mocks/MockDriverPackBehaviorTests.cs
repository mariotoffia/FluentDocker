using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Model.Drivers;
using Xunit;
using DriverContext = FluentDocker.Model.Drivers.DriverContext;

namespace FluentDocker.Tests.Mocks
{
  [Trait("Category", "Unit")]
  public class MockDriverPackBehaviorTests
  {
    [Fact]
    public async Task SetupContainerStart_DoesNotMakeAnyInspectLookRunning()
    {
      var pack = new MockDriverPack();
      await pack.InitializeAsync(new DriverContext("docker"), TestContext.Current.CancellationToken);
      pack.SetupContainerStart();

      var inspect = await pack.ContainerDriver.Object.InspectAsync(
          new DriverContext("docker"), "never-created",
          TestContext.Current.CancellationToken);

      Assert.False(inspect.Success);
      Assert.Equal(ErrorCodes.Container.NotFound, inspect.ErrorCode);
    }

    [Fact]
    public async Task SetupContainerInspect_OnlyConfiguresTheRequestedContainer()
    {
      var pack = new MockDriverPack();
      await pack.InitializeAsync(new DriverContext("docker"), TestContext.Current.CancellationToken);
      pack.SetupContainerStart()
          .SetupContainerInspect("created", running: true);

      var created = await pack.ContainerDriver.Object.InspectAsync(
          new DriverContext("docker"), "created",
          TestContext.Current.CancellationToken);
      var missing = await pack.ContainerDriver.Object.InspectAsync(
          new DriverContext("docker"), "missing",
          TestContext.Current.CancellationToken);

      Assert.True(created.Success);
      Assert.True(created.Data.State.Running);
      Assert.False(missing.Success);
      Assert.Equal(ErrorCodes.Container.NotFound, missing.ErrorCode);
    }
  }
}
