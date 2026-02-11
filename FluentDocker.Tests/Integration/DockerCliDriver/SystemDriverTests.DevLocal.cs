using System.Threading.Tasks;
using Xunit;

namespace FluentDocker.Tests.Integration.DockerCliDriver
{
  /// <summary>
  /// Docker Desktop-only tests for daemon switching.
  /// These require Docker Desktop and are not run in CI.
  /// </summary>
  [Trait("Category", "DevLocal")]
  public partial class SystemDriverTests
  {
    [Fact]
    public async Task SwitchDaemon_ExecutesWithoutException()
    {
      var result = await SystemDriver.SwitchDaemonAsync(Context);
      Assert.NotNull(result);
    }

    [Fact]
    public async Task SwitchToLinuxDaemon_ExecutesWithoutException()
    {
      var result = await SystemDriver.SwitchToLinuxDaemonAsync(Context);
      Assert.NotNull(result);
    }
  }
}
