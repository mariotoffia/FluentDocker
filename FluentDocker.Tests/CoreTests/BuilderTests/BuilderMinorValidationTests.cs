using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Model.Containers;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Volumes;
using FluentDocker.Services;
using FluentDocker.Tests.Mocks;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.BuilderTests
{
  [Trait("Category", "Unit")]
  public sealed class BuilderMinorValidationTests : MockKernelTestBase, IAsyncLifetime
  {
    public ValueTask InitializeAsync() => new(InitializeMockKernelAsync());

    [Fact]
    public void WithMemoryLimit_Negative_ThrowsAtConfigurationTime() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => NewScopedBuilder().UseContainer(c => c.WithMemoryLimit(-1)));

    [Fact]
    public void WithCpuShares_Negative_ThrowsAtConfigurationTime() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => NewScopedBuilder().UseContainer(c => c.WithCpuShares(-1)));

    [Fact]
    public void WithShmSize_Negative_ThrowsAtConfigurationTime() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => NewScopedBuilder().UseContainer(c => c.WithShmSize(-1)));

    [Fact]
    public void WithSubnet_InvalidCidr_ThrowsAtConfigurationTime() =>
        Assert.Throws<FluentDockerException>(() => NewScopedBuilder().UseNetwork(n => n.WithSubnet("garbage")));

    [Fact]
    public void ComposeWithScale_Negative_ThrowsAtConfigurationTime() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => NewScopedBuilder().UseCompose(c => c.WithScale("api", -3)));

    [Fact]
    public void ComposeWithTimeout_Negative_ThrowsAtConfigurationTime() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => NewScopedBuilder().UseCompose(c => c.WithTimeout(-10)));

    [Fact]
    public void ContainerWithEnvironment_EmptyKey_ThrowsAtConfigurationTime()
    {
      var ex = Assert.Throws<FluentDockerException>(() => NewScopedBuilder().UseContainer(c => c.WithEnvironment("=foo")));

      Assert.Contains("Expected format name=value", ex.Message);
    }

    [Fact]
    public async Task UseImage_NullName_ThrowsAtBuildTime()
    {
      var builder = NewScopedBuilder().UseImage(null!, df => df.UseParent("alpine"));

      var ex = await Assert.ThrowsAsync<FluentDockerException>(() =>
          builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken));

      Assert.Contains("without a name", ex.Message);
      MockPack.ImageDriver.Verify(d => d.BuildAsync(
          It.IsAny<DriverContext>(), It.IsAny<ImageBuildConfig>(),
          It.IsAny<IProgress<ImageBuildProgress>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ImageBuild_WithNoFromInstruction_ThrowsClearException()
    {
      SetupImageBuild();

      var ex = await Assert.ThrowsAsync<FluentDockerException>(() => NewScopedBuilder()
          .UseImage("no-from:latest", df => df.Run("echo hi"))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken));

      Assert.Contains("no FROM instruction", ex.Message);
    }

    [Fact]
    public async Task Add_MissingSource_InStrictImageBuild_ThrowsLikeCopy()
    {
      SetupImageBuild();

      var ex = await Assert.ThrowsAsync<FluentDockerException>(() => NewScopedBuilder()
          .UseImage("bad-add:latest", df => df.UseParent("alpine").Add(".out/missing-add-source.txt", "/app/"))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken));

      Assert.Contains("ADD source '.out/missing-add-source.txt' not found", ex.Message);
    }

    [Fact]
    public async Task Add_DirectorySource_InStrictImageBuild_ThrowsDocumentedRestriction()
    {
      var dir = Path.Combine(".out", "add-dir-source");
      Directory.CreateDirectory(dir);
      SetupImageBuild();

      var ex = await Assert.ThrowsAsync<NotSupportedException>(() => NewScopedBuilder()
          .UseImage("bad-add-dir:latest", df => df.UseParent("alpine").Add(dir, "/app/"))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken));

      Assert.Equal("Directory sources are not supported by DockerfileBuilder; add files individually.", ex.Message);
    }

    [Fact]
    public async Task ReuseIfExists_StartFailure_AttachesContainerLogTail()
    {
      MockPack
          .SetupContainerList(new Container { Id = "reused", Name = "web", State = new ContainerState { Running = false, Status = "exited" } })
          .SetupContainerInspect("reused", running: false)
          .SetupContainerGetLogs("reuse tail");
      MockPack.ContainerDriver
          .Setup(d => d.StartAsync(It.IsAny<DriverContext>(), "reused", It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Fail("start boom", ErrorCodes.Container.StartFailed));

      var ex = await Assert.ThrowsAsync<ContainerStartException>(() => NewScopedBuilder()
          .UseContainer(c => c.UseImage("alpine").WithName("web").ReuseIfExists())
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken));

      Assert.Equal("reuse tail", ex.Data["ContainerLogTail"]);
    }

    [Fact]
    public async Task RetryAfterFailedCleanup_PreservesBuilderCreatedVolumeOwnership()
    {
      var inspectCalls = 0;
      MockPack.SetupVolumeCreate("owned-vol");
      MockPack.VolumeDriver
          .Setup(d => d.InspectAsync(It.IsAny<DriverContext>(), "owned-vol", It.IsAny<CancellationToken>()))
          .ReturnsAsync(() => inspectCalls++ == 0
              ? CommandResponse<Volume>.Fail("missing")
              : CommandResponse<Volume>.Ok(new Volume { Name = "owned-vol", Driver = "local" }));
      MockPack.VolumeDriver
          .Setup(d => d.RemoveAsync(It.IsAny<DriverContext>(), "owned-vol", true, It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Fail("cleanup missed"));
      MockPack.ContainerDriver
          .SetupSequence(d => d.CreateAsync(It.IsAny<DriverContext>(), It.IsAny<ContainerCreateConfig>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<ContainerCreateResult>.Fail("first boom"))
          .ReturnsAsync(CommandResponse<ContainerCreateResult>.Ok(new ContainerCreateResult { Id = "retry-container" }));
      MockPack
          .SetupContainerStart()
          .SetupContainerInspect("retry-container", running: true)
          .SetupContainerStop()
          .SetupContainerRemove();
      var builder = NewScopedBuilder()
          .UseVolume(v => v.WithName("owned-vol").RemoveOnDispose())
          .UseContainer(c => c.UseImage("alpine"));

      await Assert.ThrowsAsync<DriverException>(() => builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken));
      await using var results = await builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken);
      var volume = Assert.IsAssignableFrom<IVolumeService>(results.All.Single(s => s is IVolumeService));

      await volume.DisposeAsync();
      MockPack.VolumeDriver.Verify(d => d.RemoveAsync(It.IsAny<DriverContext>(), "owned-vol", true, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task RetryAfterFailedCleanup_PreservesBuilderCreatedNetworkOwnershipById()
    {
      var listCalls = 0;
      MockPack.NetworkDriver
          .Setup(d => d.ListAsync(It.IsAny<DriverContext>(), null!, It.IsAny<CancellationToken>()))
          .ReturnsAsync(() => CommandResponse<IList<Network>>.Ok(listCalls++ == 0
              ? []
              : [new Network { Id = "owned-net", Name = "owned-net" }]));
      MockPack.SetupNetworkCreate("owned-net");
      MockPack.NetworkDriver
          .Setup(d => d.RemoveAsync(It.IsAny<DriverContext>(), "owned-net", It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Fail("cleanup missed"));
      MockPack.ContainerDriver
          .SetupSequence(d => d.CreateAsync(It.IsAny<DriverContext>(), It.IsAny<ContainerCreateConfig>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<ContainerCreateResult>.Fail("first boom"))
          .ReturnsAsync(CommandResponse<ContainerCreateResult>.Ok(new ContainerCreateResult { Id = "retry-container" }));
      MockPack
          .SetupContainerStart()
          .SetupContainerInspect("retry-container", running: true)
          .SetupContainerStop()
          .SetupContainerRemove();
      var builder = NewScopedBuilder()
          .UseNetwork(n => n.WithName("owned-net").RemoveOnDispose())
          .UseContainer(c => c.UseImage("alpine"));

      await Assert.ThrowsAsync<DriverException>(() => builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken));
      await using var results = await builder.BuildAsync(cancellationToken: TestContext.Current.CancellationToken);
      var network = Assert.IsAssignableFrom<INetworkService>(results.All.Single(s => s is INetworkService));

      await network.DisposeAsync();
      MockPack.NetworkDriver.Verify(d => d.RemoveAsync(It.IsAny<DriverContext>(), "owned-net", It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    private Builder NewScopedBuilder() => new Builder().WithinDriver(DriverId, Kernel);

    private void SetupImageBuild()
    {
      MockPack.ImageDriver
          .Setup(d => d.BuildAsync(It.IsAny<DriverContext>(), It.IsAny<ImageBuildConfig>(), It.IsAny<IProgress<ImageBuildProgress>>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<ImageBuildResult>.Ok(new ImageBuildResult { ImageId = "sha256:test" }));
    }
  }
}
