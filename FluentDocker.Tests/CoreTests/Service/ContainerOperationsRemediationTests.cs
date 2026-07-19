using System;
using System.IO;
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
  public class ContainerOperationsRemediationTests : MockKernelTestBase, IAsyncLifetime
  {
    public async ValueTask InitializeAsync()
    {
      await InitializeMockKernelAsync();
    }

    [Fact]
    public async Task CopyFromAsync_WhenDriverCopiesDirectory_ThrowsDomainError()
    {
      MockPack.ContainerDriver
          .Setup(d => d.CopyFromAsync(
              It.IsAny<DriverContext>(), "container-123", "/var/log",
              It.IsAny<string>(), It.IsAny<CancellationToken>()))
          .Callback<DriverContext, string, string, string, CancellationToken>((_, _, _, path, _) =>
              Directory.CreateDirectory(path))
          .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));
      var service = new ContainerService(Kernel, DriverId, "container-123", "alpine", "test");

      var error = await Assert.ThrowsAsync<FluentDockerException>(() =>
          service.CopyFromAsync("/var/log", TestContext.Current.CancellationToken));

      Assert.Contains("directory", error.Message, StringComparison.OrdinalIgnoreCase);
      Assert.Contains(nameof(IContainerService.CopyFromToPathAsync), error.Message);
    }
  }
}
