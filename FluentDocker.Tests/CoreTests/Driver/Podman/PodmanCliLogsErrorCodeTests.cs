using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers.Podman.Cli.Binary;
using FluentDocker.Drivers.Podman.Cli.Components;
using FluentDocker.Model.Drivers;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Podman
{
  /// <summary>
  /// POD-6: the <c>GetLogsAsync</c> catch-all must fall back to
  /// <see cref="ErrorCodes.Container.LogsFailed"/> (the operation's honest code), not
  /// <see cref="ErrorCodes.General.Unknown"/>.
  /// </summary>
  [Trait("Category", "Unit")]
  public class PodmanCliLogsErrorCodeTests
  {
    [Fact]
    public async Task GetLogsAsync_WhenExecutionThrowsUncodedException_FailsWithLogsFailed()
    {
      // An uncoded FluentDockerException (e.g. binary resolution failure) exercises the
      // catch-all fallback code path.
      var resolver = new Mock<IPodmanBinaryResolver>();
      resolver.Setup(r => r.Resolve(It.IsAny<string>()))
          .Throws(new FluentDockerException("podman binary not found"));
      var driver = new PodmanCliContainerDriver(resolver.Object);
      driver.Initialize(new DriverContext("podman"));

      var result = await driver.GetLogsAsync(
          new DriverContext("podman"), "ctr", cancellationToken: TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Container.LogsFailed, result.ErrorCode);
      Assert.Contains("podman binary not found", result.Error);
    }
  }
}
