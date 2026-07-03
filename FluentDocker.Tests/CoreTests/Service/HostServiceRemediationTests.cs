using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Model.Containers;
using FluentDocker.Model.Drivers;
using FluentDocker.Services;
using FluentDocker.Services.Impl;
using FluentDocker.Tests.Mocks;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Service
{
  [Trait("Category", "Unit")]
  public class HostServiceRemediationTests : MockKernelTestBase, IAsyncLifetime
  {
    public async ValueTask InitializeAsync()
    {
      await InitializeMockKernelAsync();
    }

    [Theory]
    [InlineData("redis:7", "redis", "7")]
    [InlineData("localhost:5000/acme/api:1.2", "localhost:5000/acme/api", "1.2")]
    [InlineData("acme/api@sha256:abcdef", "acme/api@sha256:abcdef", null)]
    public async Task CreateContainerAsync_ForcePull_PreservesImageReference(
        string image,
        string expectedImage,
        string? expectedTag)
    {
      MockPack.SetupContainerCreate("container-123");
      string? actualImage = null;
      string? actualTag = null;
      MockPack.ImageDriver
          .Setup(d => d.PullAsync(
              It.IsAny<DriverContext>(), It.IsAny<string>(), It.IsAny<string>(),
              It.IsAny<IProgress<ImagePullProgress>>(), It.IsAny<CancellationToken>()))
          .Callback<DriverContext, string, string, IProgress<ImagePullProgress>, CancellationToken>(
              (_, pulledImage, tag, _, _) =>
              {
                actualImage = pulledImage;
                actualTag = tag;
              })
          .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));
      var service = new HostService(Kernel, DriverId, "host");

      await service.CreateContainerAsync(
          image,
          new ContainerCreateOptions { ForcePull = true },
          TestContext.Current.CancellationToken);

      Assert.Equal(expectedImage, actualImage);
      Assert.Equal(expectedTag, actualTag);
    }

    [Theory]
    [InlineData(@"C:\data:/data", @"C:\data", "/data")]
    [InlineData("/h:/c:ro", "/h", "/c:ro")]
    [InlineData("/h:/c", "/h", "/c")]
    public async Task CreateContainerAsync_ParsesVolumeSpecs(
        string volumeSpec,
        string expectedHost,
        string expectedContainer)
    {
      ContainerCreateConfig? captured = null;
      MockPack.ContainerDriver
          .Setup(d => d.CreateAsync(
              It.IsAny<DriverContext>(), It.IsAny<ContainerCreateConfig>(),
              It.IsAny<CancellationToken>()))
          .Callback<DriverContext, ContainerCreateConfig, CancellationToken>((_, config, _) =>
              captured = config)
          .ReturnsAsync(CommandResponse<ContainerCreateResult>.Ok(
              new ContainerCreateResult { Id = "container-123" }));
      var service = new HostService(Kernel, DriverId, "host");

      await service.CreateContainerAsync(
          "alpine",
          new ContainerCreateOptions { Volumes = [volumeSpec] },
          TestContext.Current.CancellationToken);

      Assert.NotNull(captured);
      Assert.True(captured.Volumes.TryGetValue(expectedHost, out var container));
      Assert.Equal(expectedContainer, container);
    }

    [Fact]
    public async Task GetContainersAsync_NameFilter_MapsToFilterName()
    {
      ContainerListFilter? captured = null;
      MockPack.ContainerDriver
          .Setup(d => d.ListAsync(
              It.IsAny<DriverContext>(), It.IsAny<ContainerListFilter>(),
              It.IsAny<CancellationToken>()))
          .Callback<DriverContext, ContainerListFilter, CancellationToken>((_, filter, _) =>
              captured = filter)
          .ReturnsAsync(CommandResponse<IList<Container>>.Ok([]));
      var service = new HostService(Kernel, DriverId, "host");

      await service.GetContainersAsync(
          filters: new Dictionary<string, string> { ["name"] = "web" },
          cancellationToken: TestContext.Current.CancellationToken);

      Assert.NotNull(captured);
      Assert.Equal("web", captured.Name);
      Assert.Empty(captured.Labels);
    }

    [Fact]
    public async Task GetContainersAsync_UnknownFilter_RemainsLabel()
    {
      ContainerListFilter? captured = null;
      MockPack.ContainerDriver
          .Setup(d => d.ListAsync(
              It.IsAny<DriverContext>(), It.IsAny<ContainerListFilter>(),
              It.IsAny<CancellationToken>()))
          .Callback<DriverContext, ContainerListFilter, CancellationToken>((_, filter, _) =>
              captured = filter)
          .ReturnsAsync(CommandResponse<IList<Container>>.Ok([]));
      var service = new HostService(Kernel, DriverId, "host");

      await service.GetContainersAsync(
          filters: new Dictionary<string, string> { ["com.acme.role"] = "web" },
          cancellationToken: TestContext.Current.CancellationToken);

      Assert.NotNull(captured);
      Assert.Equal("web", captured.Labels["com.acme.role"]);
    }

    [Fact]
    public void AddHook_ThrowsFluentDockerNotSupportedException()
    {
      var service = new HostService(Kernel, DriverId, "host");

      Assert.Throws<FluentDockerNotSupportedException>(() =>
          service.AddHook(ServiceRunningState.Running, _ => Task.CompletedTask));
    }
  }
}
