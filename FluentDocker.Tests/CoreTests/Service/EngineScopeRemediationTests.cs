using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
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

    [Fact]
    public async Task CreateAsync_WhenSwitchFails_ThrowsDriverException()
    {
      var mockPack = new MockDriverPack();
      mockPack.SetupSystemIsWindowsEngine(true);
      mockPack.SystemDriver
          .Setup(d => d.SwitchToLinuxDaemonAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Fail("switch failed"));
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        await Assert.ThrowsAsync<DriverException>(() =>
            EngineScope.CreateAsync(
                kernel, "docker", EngineScopeType.Linux, TestContext.Current.CancellationToken));
      }
      finally
      {
        kernel.Dispose();
      }
    }
  }
}
