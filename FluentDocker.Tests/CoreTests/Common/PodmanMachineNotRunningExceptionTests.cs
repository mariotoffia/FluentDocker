using FluentDocker.Common;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Common
{
  [Trait("Category", "Unit")]
  public class PodmanMachineNotRunningExceptionTests
  {
    [Fact]
    public void Constructor_SetsDriverExceptionContract()
    {
      var exception = new PodmanMachineNotRunningException("podman machine is stopped");

      Assert.IsAssignableFrom<DriverException>(exception);
      Assert.Equal(ErrorCodes.Machine.NotRunning, exception.ErrorCode);
      Assert.True(exception.IsTransient);
    }
  }
}
