using System;
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
  /// <summary>
  /// Unit tests for <c>ForcePullImage()</c>: the configured reference must be pulled verbatim
  /// (not clobbered to <c>latest</c>) and pull failures must surface as a
  /// <see cref="DriverException"/> instead of being silently swallowed.
  /// </summary>
  public partial class BuilderContainerTests
  {
    [Fact]
    public async Task WithRegistryAuth_LogsInBeforePull()
    {
      // TST-MAJ-5: WithRegistryAuth authenticates via IAuthDriver before the image is pulled, so a
      // private-registry image works without hand-resolving the driver layer.
      var auth = new Mock<IAuthDriver>();
      RegistryLoginConfig captured = null;
      auth.Setup(a => a.LoginAsync(It.IsAny<DriverContext>(), It.IsAny<RegistryLoginConfig>(), It.IsAny<CancellationToken>()))
          .Callback<DriverContext, RegistryLoginConfig, CancellationToken>((_, cfg, _) => captured = cfg)
          .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));
      MockPack.RegisterCustomDriver(auth.Object);
      MockPack
          .SetupImagePull()
          .SetupContainerCreate("container-123")
          .SetupContainerStart()
          .SetupContainerInspect("container-123", running: true)
          .SetupContainerStop()
          .SetupContainerRemove();

      await new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c
              .UseImage("registry.example.com/team/app:latest")
              .WithRegistryAuth("me", "secret", "registry.example.com")
              .ForcePullImage()
              .WithName("web"))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);

      auth.Verify(a => a.LoginAsync(
          It.IsAny<DriverContext>(), It.IsAny<RegistryLoginConfig>(), It.IsAny<CancellationToken>()), Times.Once);
      Assert.NotNull(captured);
      Assert.Equal("me", captured.Username);
      Assert.Equal("secret", captured.Password);
      Assert.Equal("registry.example.com", captured.Server);
    }

    [Fact]
    public async Task ForcePullImage_TaggedReference_PullsExactTagNotLatest()
    {
      MockPack
          .SetupImagePull()
          .SetupContainerCreate("container-123")
          .SetupContainerStart()
          .SetupContainerInspect("container-123", running: true)
          .SetupContainerStop()
          .SetupContainerRemove();

      await new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c
              .UseImage("nginx:alpine")
              .ForcePullImage()
              .WithName("web"))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);

      // Repository + exact tag — never the bogus "latest" on an already-tagged ref.
      MockPack.ImageDriver.Verify(d => d.PullAsync(
          It.IsAny<DriverContext>(),
          "nginx",
          "alpine",
          It.IsAny<IProgress<ImagePullProgress>>(),
          It.IsAny<CancellationToken>()), Times.Once);

      MockPack.ImageDriver.Verify(d => d.PullAsync(
          It.IsAny<DriverContext>(),
          It.IsAny<string>(),
          "latest",
          It.IsAny<IProgress<ImagePullProgress>>(),
          It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ForcePullImage_RegistryPortReferenceWithoutTag_DefaultsToLatest()
    {
      MockPack
          .SetupImagePull()
          .SetupContainerCreate("container-123")
          .SetupContainerStart()
          .SetupContainerInspect("container-123", running: true)
          .SetupContainerStop()
          .SetupContainerRemove();

      await new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c
              .UseImage("myregistry:5000/img")
              .ForcePullImage()
              .WithName("svc"))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken);

      // The registry host:port colon must not be mistaken for a tag.
      MockPack.ImageDriver.Verify(d => d.PullAsync(
          It.IsAny<DriverContext>(),
          "myregistry:5000/img",
          "latest",
          It.IsAny<IProgress<ImagePullProgress>>(),
          It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ForcePullImage_PullFailure_ThrowsDriverException()
    {
      MockPack.ImageDriver
          .Setup(d => d.PullAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              It.IsAny<string>(),
              It.IsAny<IProgress<ImagePullProgress>>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Fail("registry unreachable"));

      var ex = await Assert.ThrowsAsync<DriverException>(() => new Builder()
          .WithinDriver(DriverId, Kernel)
          .UseContainer(c => c
              .UseImage("nginx:alpine")
              .ForcePullImage()
              .WithName("web"))
          .BuildAsync(cancellationToken: TestContext.Current.CancellationToken));

      Assert.Equal(ErrorCodes.Image.PullFailed, ex.ErrorCode);
      Assert.Contains("nginx:alpine", ex.Message);

      // A failed pull must abort the build before the container is created.
      MockPack.ContainerDriver.Verify(d => d.CreateAsync(
          It.IsAny<DriverContext>(),
          It.IsAny<ContainerCreateConfig>(),
          It.IsAny<CancellationToken>()), Times.Never);
    }
  }
}
