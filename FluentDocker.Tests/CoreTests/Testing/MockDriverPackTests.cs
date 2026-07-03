using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Model.Drivers;
using FluentDocker.Tests.Mocks;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Testing
{
  [Trait("Category", "Unit")]
  public class MockDriverPackTests
  {
    [Fact]
    public async Task SetupContainerInspect_MatchesRequestedId()
    {
      var pack = new MockDriverPack();
      await pack.InitializeAsync(
          new DriverContext("docker"), TestContext.Current.CancellationToken);
      pack.SetupContainerInspect("expected");

      var matched = await pack.ContainerDriver.Object.InspectAsync(
          new DriverContext("docker"),
          "expected",
          TestContext.Current.CancellationToken);
      var unmatched = await pack.ContainerDriver.Object.InspectAsync(
          new DriverContext("docker"),
          "other",
          TestContext.Current.CancellationToken);

      Assert.True(matched.Success);
      Assert.False(unmatched.Success);
      Assert.Equal(FluentDocker.Model.Drivers.ErrorCodes.Container.NotFound, unmatched.ErrorCode);
    }

    [Fact]
    public async Task SetupNetworkInspect_MatchesRequestedId()
    {
      var pack = new MockDriverPack();
      await pack.InitializeAsync(
          new DriverContext("docker"), TestContext.Current.CancellationToken);
      pack.SetupNetworkInspect("expected-net");

      var matched = await pack.NetworkDriver.Object.InspectAsync(
          new DriverContext("docker"),
          "expected-net",
          TestContext.Current.CancellationToken);
      var unmatched = await pack.NetworkDriver.Object.InspectAsync(
          new DriverContext("docker"),
          "other-net",
          TestContext.Current.CancellationToken);

      Assert.True(matched.Success);
      Assert.Null(unmatched);
    }
  }
}
