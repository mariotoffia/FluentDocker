using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Model.Drivers;
using FluentDocker.Services.Impl;
using FluentDocker.Tests.Mocks;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Service
{
  [Trait("Category", "Unit")]
  public class ContainerServiceDockerApiCopyFromTests : MockKernelTestBase, IAsyncLifetime
  {
    public async ValueTask InitializeAsync()
    {
      await InitializeMockKernelAsync();
    }

    [Fact]
    public async Task CopyFromAsync_UsesNonExistingFileDestinationForDriver()
    {
      MockPack.ContainerDriver
          .Setup(d => d.CopyFromAsync(
              It.IsAny<DriverContext>(),
              "container-123",
              "/data/file.txt",
              It.IsAny<string>(),
              It.IsAny<CancellationToken>()))
          .Callback<DriverContext, string, string, string, CancellationToken>((_, _, _, path, _) =>
          {
            Assert.False(File.Exists(path));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "copied");
          })
          .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));
      var service = new ContainerService(Kernel, DriverId, "container-123", "alpine", "test");

      var bytes = await service.CopyFromAsync(
          "/data/file.txt", TestContext.Current.CancellationToken);

      Assert.Equal("copied", System.Text.Encoding.UTF8.GetString(bytes));
    }
  }
}
