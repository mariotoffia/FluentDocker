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

    [Fact]
    public async Task BuildAsync_RetryReownsOwnLeftoverComposeProject_TearsDownOnDispose()
    {
      // Attempt 1's probe finds nothing (empty list); attempt 2's probe finds the leftover
      // this builder itself created on attempt 1 but failed to clean up.
      var listCalls = 0;
      MockPack.ComposeDriver
          .Setup(d => d.ListAsync(
              It.IsAny<DriverContext>(), It.IsAny<ComposeListConfig>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(() => CommandResponse<IList<ComposeServiceInfo>>.Ok(listCalls++ == 0
              ? []
              : [new ComposeServiceInfo { Name = "web", Project = "retry-project" }]));
      MockPack.ComposeDriver
          .SetupSequence(d => d.UpAsync(
              It.IsAny<DriverContext>(), It.IsAny<ComposeUpConfig>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<ComposeUpResult>.Fail("up failed"))
          .ReturnsAsync(CommandResponse<ComposeUpResult>.Ok(new ComposeUpResult { ProjectName = "retry-project" }));
      MockPack.ComposeDriver
          .SetupSequence(d => d.DownAsync(
              It.IsAny<DriverContext>(), It.IsAny<ComposeDownConfig>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Fail("down failed"))
          .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));

      var builder = new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseCompose(c => c.WithProjectName("retry-project"));

      // Attempt 1: up fails and best-effort cleanup (down) also fails, so the half-created
      // project survives (simulated by attempt 2's probe finding it below).
      await Assert.ThrowsAsync<DriverException>(
          () => builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken));

      // Retry on the SAME builder instance -- the only supported retry contract (Builder.cs:336-338).
      await using (var results = await builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken))
      {
        var compose = Assert.Single(results.ComposeServices);
        Assert.False(compose.IsBorrowed);
      }

      // One down from attempt 1's failed cleanup, one from attempt 2's successful dispose-time
      // teardown -- the re-owned leftover must actually be torn down, not leaked.
      MockPack.ComposeDriver.Verify(d => d.DownAsync(
          It.IsAny<DriverContext>(), It.IsAny<ComposeDownConfig>(),
          It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task BuildAsync_ConsecutiveFailedRetries_CreatedMarkerSurvivesCascade_FinalRetryReownsAndTearsDown()
    {
      // N>=2 failure cascade: attempt 1 creates the project and fails (up + cleanup down both
      // fail); attempt 2 re-owns the leftover and fails the same way; attempt 3 re-owns and
      // succeeds. The created-marker must survive EVERY ResetForRetry in the cascade — losing it
      // on the second reset would silently downgrade attempt 3 to "borrowed" and leak the stack.
      var listCalls = 0;
      MockPack.ComposeDriver
          .Setup(d => d.ListAsync(
              It.IsAny<DriverContext>(), It.IsAny<ComposeListConfig>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(() => CommandResponse<IList<ComposeServiceInfo>>.Ok(listCalls++ == 0
              ? []
              : [new ComposeServiceInfo { Name = "web", Project = "cascade-project" }]));
      MockPack.ComposeDriver
          .SetupSequence(d => d.UpAsync(
              It.IsAny<DriverContext>(), It.IsAny<ComposeUpConfig>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<ComposeUpResult>.Fail("up failed (attempt 1)"))
          .ReturnsAsync(CommandResponse<ComposeUpResult>.Fail("up failed (attempt 2)"))
          .ReturnsAsync(CommandResponse<ComposeUpResult>.Ok(new ComposeUpResult { ProjectName = "cascade-project" }));
      MockPack.ComposeDriver
          .SetupSequence(d => d.DownAsync(
              It.IsAny<DriverContext>(), It.IsAny<ComposeDownConfig>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Fail("down failed (attempt 1)"))
          .ReturnsAsync(CommandResponse<Unit>.Fail("down failed (attempt 2)"))
          .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));

      var builder = new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseCompose(c => c.WithProjectName("cascade-project"));

      await Assert.ThrowsAsync<DriverException>(
          () => builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken));
      await Assert.ThrowsAsync<DriverException>(
          () => builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken));

      await using (var results = await builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken))
      {
        var compose = Assert.Single(results.ComposeServices);
        Assert.False(compose.IsBorrowed);
      }

      // Two failed cleanup downs (attempts 1-2) + the successful dispose-time teardown.
      MockPack.ComposeDriver.Verify(d => d.DownAsync(
          It.IsAny<DriverContext>(), It.IsAny<ComposeDownConfig>(),
          It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task BuildAsync_RetryWithGenuinePreExistingComposeProject_NeverReownsOrTearsDown()
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

      var builder = new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseCompose(c => c.WithProjectName("shared").WithRemoveVolumes(true))
          .UseContainer(c => c.UseImage("alpine"));

      // Control: a genuinely pre-existing project (this builder's created-marker is never set,
      // since it never owned the project) must stay borrowed across retries too -- re-own must
      // never over-reach into a resource this builder did not create.
      await Assert.ThrowsAsync<DriverException>(
          () => builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken));
      await Assert.ThrowsAsync<DriverException>(
          () => builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken));

      MockPack.ComposeDriver.Verify(d => d.DownAsync(
          It.IsAny<DriverContext>(), It.IsAny<ComposeDownConfig>(),
          It.IsAny<CancellationToken>()), Times.Never);
    }
  }
}
