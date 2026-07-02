using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Model.Drivers;
using FluentDocker.Tests.Mocks;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.BuilderTests
{
  public partial class BuilderComposeTests
  {
    [Fact]
    public async Task UseCompose_WhenUpFails_RunsBestEffortDownBeforeThrowing()
    {
      var (kernel, mockPack) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");
      mockPack.ComposeDriver
          .Setup(d => d.UpAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<ComposeUpConfig>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<ComposeUpResult>.Fail("up failed"));
      mockPack.SetupComposeDown();

      try
      {
        await Assert.ThrowsAsync<DriverException>(() =>
            new Builder()
                .WithinDriver("docker", kernel)
                .UseCompose(c => c
                    .WithComposeFile("docker-compose.yml")
                    .WithProjectName("failed-project"))
                .BuildAsync(cancellationToken: TestContext.Current.CancellationToken));

        mockPack.ComposeDriver.Verify(d => d.DownAsync(
            It.IsAny<DriverContext>(),
            It.Is<ComposeDownConfig>(c =>
                c.ProjectName == "failed-project" &&
                c.ComposeFiles.Contains("docker-compose.yml")),
            It.IsAny<CancellationToken>()), Times.Once);
      }
      finally { kernel.Dispose(); }
    }
  }
}
