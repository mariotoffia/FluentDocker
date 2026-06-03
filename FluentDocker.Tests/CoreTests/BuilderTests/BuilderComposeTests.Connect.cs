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
  /// <summary>
  /// Tests for attaching to an existing compose project (issue #305): ConnectToExisting()
  /// must return a usable IComposeService without running `docker compose up`.
  /// </summary>
  public partial class BuilderComposeTests
  {
    [Fact]
    public async Task ConnectToExisting_DoesNotRunUp_AndReturnsService()
    {
      var (kernel, mockPack) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");
      try
      {
        await using var results = await new Builder()
            .WithinDriver("docker", kernel)
            .UseCompose(c => c
                .WithProjectName("existing-proj")
                .ConnectToExisting())
            .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);

        // `up` must never be invoked when attaching to an existing project.
        mockPack.ComposeDriver.Verify(d => d.UpAsync(
            It.IsAny<DriverContext>(),
            It.IsAny<ComposeUpConfig>(),
            It.IsAny<System.Threading.CancellationToken>()), Times.Never);

        Assert.Single(results.ComposeServices);
        Assert.Equal("existing-proj", results.ComposeServices[0].ProjectName);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task ConnectToExisting_WithoutProjectOrFiles_Throws()
    {
      var (kernel, mockPack) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");
      try
      {
        await Assert.ThrowsAsync<FluentDockerException>(async () =>
            await new Builder()
                .WithinDriver("docker", kernel)
                .UseCompose(c => c.ConnectToExisting())
                .BuildAsync(cancellationToken: TestContext.Current.CancellationToken));

        mockPack.ComposeDriver.Verify(d => d.UpAsync(
            It.IsAny<DriverContext>(),
            It.IsAny<ComposeUpConfig>(),
            It.IsAny<System.Threading.CancellationToken>()), Times.Never);
      }
      finally { kernel.Dispose(); }
    }
  }
}
