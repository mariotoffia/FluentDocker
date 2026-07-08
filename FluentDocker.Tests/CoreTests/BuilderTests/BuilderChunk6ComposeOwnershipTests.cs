using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Model.Containers;
using FluentDocker.Model.Drivers;
using FluentDocker.Tests.Mocks;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.BuilderTests
{
  [Trait("Category", "Unit")]
  public sealed class BuilderChunk6ComposeOwnershipTests : MockKernelTestBase, IAsyncLifetime
  {
    public ValueTask InitializeAsync() => new(InitializeMockKernelAsync());

    [Fact]
    public async Task BorrowedComposeProject_WhenSiblingOperationFails_IsNotDownedDuringRollback()
    {
      MockPack
          .SetupComposeList(new ComposeServiceInfo { Name = "web", Project = "shared" })
          .SetupComposeUp("shared")
          .SetupComposeDown();
      MockPack.ContainerDriver
          .Setup(d => d.CreateAsync(
              It.IsAny<DriverContext>(), It.IsAny<ContainerCreateConfig>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<ContainerCreateResult>.Fail("boom"));

      await Assert.ThrowsAsync<DriverException>(() => new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseCompose(c => c.WithProjectName("shared").WithRemoveVolumes(true))
          .UseContainer(c => c.UseImage("alpine"))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken));

      MockPack.ComposeDriver.Verify(d => d.DownAsync(
          It.IsAny<DriverContext>(), It.IsAny<ComposeDownConfig>(),
          It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task BorrowedComposeProject_WhenBuildResultsDisposed_IsNotDowned()
    {
      MockPack
          .SetupComposeList(new ComposeServiceInfo { Name = "web", Project = "shared" })
          .SetupComposeUp("shared")
          .SetupComposeDown();

      await using (var results = await new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseCompose(c => c.WithProjectName("shared").WithRemoveVolumes(true))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken))
      {
        Assert.Single(results.ComposeServices);
      }

      MockPack.ComposeDriver.Verify(d => d.DownAsync(
          It.IsAny<DriverContext>(), It.IsAny<ComposeDownConfig>(),
          It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SelfCreatedComposeProject_WhenBuildResultsDisposed_IsDowned()
    {
      MockPack
          .SetupComposeList()
          .SetupComposeUp("owned")
          .SetupComposeDown();

      await using (var results = await new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseCompose(c => c.WithProjectName("owned").WithRemoveVolumes(true))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken))
      {
        Assert.Single(results.ComposeServices);
      }

      MockPack.ComposeDriver.Verify(d => d.DownAsync(
          It.IsAny<DriverContext>(),
          It.Is<ComposeDownConfig>(cfg => cfg.ProjectName == "owned" && cfg.RemoveVolumes),
          It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InconclusiveComposeBorrowProbe_WhenUpFails_DoesNotDownProject(bool throwProbe)
    {
      if (throwProbe)
      {
        MockPack.ComposeDriver
            .Setup(d => d.ListAsync(
                It.IsAny<DriverContext>(), It.IsAny<ComposeListConfig>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("probe failed"));
      }
      else
      {
        MockPack.ComposeDriver
            .Setup(d => d.ListAsync(
                It.IsAny<DriverContext>(), It.IsAny<ComposeListConfig>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CommandResponse<IList<ComposeServiceInfo>>.Fail("probe failed"));
      }
      MockPack.ComposeDriver
          .Setup(d => d.UpAsync(
              It.IsAny<DriverContext>(), It.IsAny<ComposeUpConfig>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<ComposeUpResult>.Fail("up failed"));
      MockPack.SetupComposeDown();

      await Assert.ThrowsAsync<DriverException>(() => new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseCompose(c => c.WithProjectName("shared").WithRemoveVolumes(true))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken));

      MockPack.ComposeDriver.Verify(d => d.DownAsync(
          It.IsAny<DriverContext>(), It.IsAny<ComposeDownConfig>(),
          It.IsAny<CancellationToken>()), Times.Never);
    }
  }
}
