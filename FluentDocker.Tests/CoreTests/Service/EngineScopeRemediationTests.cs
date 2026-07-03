using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Model.Drivers;
using FluentDocker.Services;
using FluentDocker.Services.Impl;
using FluentDocker.Tests.Mocks;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Service
{
  [Trait("Category", "Unit")]
  public class EngineScopeRemediationTests
  {
    [Fact]
    public async Task DisposeAsync_CalledTwice_RestoresOriginalScopeOnce()
    {
      var mockPack = new MockDriverPack();
      mockPack.SetupSystemIsWindowsEngine(false);
      mockPack.SetupSystemSwitchToWindows();
      mockPack.SetupSystemSwitchToLinux();
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var scope = await EngineScope.CreateAsync(
            kernel, "docker", EngineScopeType.Windows, TestContext.Current.CancellationToken);

        await scope.DisposeAsync();
        await scope.DisposeAsync();

        mockPack.SystemDriver.Verify(d => d.SwitchToLinuxDaemonAsync(
            It.IsAny<DriverContext>(), It.IsAny<CancellationToken>()), Times.Once);
      }
      finally
      {
        kernel.Dispose();
      }
    }
  }
}
