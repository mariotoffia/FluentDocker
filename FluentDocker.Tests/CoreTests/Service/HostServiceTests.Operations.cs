using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Containers;
using FluentDocker.Model.Drivers;
using FluentDocker.Services;
using FluentDocker.Services.Impl;
using FluentDocker.Tests.Mocks;
using Moq;
using Xunit;

#pragma warning disable CS0618 // IService obsolete -- intentional test usage

namespace FluentDocker.Tests.CoreTests.Service
{
  /// <summary>
  /// Tests for HostService image and container operations
  /// from HostService.Operations.cs.
  /// </summary>
  [Trait("Category", "Unit")]
  public class HostServiceOperationsTests
  {
    #region Image Management -- GetImagesAsync

    [Fact]
    public async Task GetImagesAsync_Success_ReturnsImageServices()
    {
      var mockPack = new MockDriverPack();
      mockPack.ImageDriver
          .Setup(d => d.ListAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<ImageListFilter>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<IList<Image>>.Ok(new List<Image>
          {
            new Image { Id = "sha256:aaa", RepoTags = new List<string> { "nginx:latest" } },
            new Image { Id = "sha256:bbb", RepoTags = new List<string> { "redis:7-alpine" } }
          }));

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = new HostService(kernel, "docker", "test-host");
        var images = await service.GetImagesAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(images);
        Assert.Equal(2, images.Count);
        mockPack.ImageDriver.Verify(d => d.ListAsync(
            It.IsAny<DriverContext>(),
            It.Is<ImageListFilter>(f => f.All),
            It.IsAny<CancellationToken>()), Times.Once);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task GetImagesAsync_WithCustomFilter_PassesFilterToDriver()
    {
      var mockPack = new MockDriverPack();
      mockPack.ImageDriver
          .Setup(d => d.ListAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<ImageListFilter>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<IList<Image>>.Ok(new List<Image>()));

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = new HostService(kernel, "docker", "test-host");
        var filter = new ImageListFilter { All = false };
        var images = await service.GetImagesAsync(
            all: false, filter: filter,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(images);
        Assert.Empty(images);
        mockPack.ImageDriver.Verify(d => d.ListAsync(
            It.IsAny<DriverContext>(),
            It.Is<ImageListFilter>(f => f == filter),
            It.IsAny<CancellationToken>()), Times.Once);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task GetImagesAsync_Failure_ThrowsDriverException()
    {
      var mockPack = new MockDriverPack();
      mockPack.ImageDriver
          .Setup(d => d.ListAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<ImageListFilter>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<IList<Image>>.Fail("list images failed"));

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = new HostService(kernel, "docker", "test-host");
        var ex = await Assert.ThrowsAsync<DriverException>(
            () => service.GetImagesAsync(
                cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("list images", ex.Message);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task GetImagesAsync_EmptyRepoTags_HandlesGracefully()
    {
      var mockPack = new MockDriverPack();
      mockPack.ImageDriver
          .Setup(d => d.ListAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<ImageListFilter>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<IList<Image>>.Ok(new List<Image>
          {
            new Image { Id = "sha256:ccc", RepoTags = new List<string>() }
          }));

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = new HostService(kernel, "docker", "test-host");
        var images = await service.GetImagesAsync(
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Single(images);
      }
      finally { kernel.Dispose(); }
    }

    #endregion

    #region Image Management -- PullImageAsync

    [Fact]
    public async Task PullImageAsync_Success_ReturnsImageService()
    {
      var mockPack = new MockDriverPack();
      mockPack.SetupImagePull();
      mockPack.SetupImageInspect("sha256:pulled123");

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = new HostService(kernel, "docker", "test-host");
        var image = await service.PullImageAsync(
            "nginx", "latest",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(image);
        mockPack.ImageDriver.Verify(d => d.PullAsync(
            It.IsAny<DriverContext>(),
            It.Is<string>(s => s == "nginx"),
            It.Is<string>(s => s == "latest"),
            It.IsAny<IProgress<ImagePullProgress>>(),
            It.IsAny<CancellationToken>()), Times.Once);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task PullImageAsync_WithCustomTag_PassesTag()
    {
      var mockPack = new MockDriverPack();
      mockPack.SetupImagePull();
      mockPack.SetupImageInspect("sha256:alpine311");

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = new HostService(kernel, "docker", "test-host");
        await service.PullImageAsync(
            "alpine", "3.11",
            cancellationToken: TestContext.Current.CancellationToken);

        mockPack.ImageDriver.Verify(d => d.PullAsync(
            It.IsAny<DriverContext>(),
            It.Is<string>(s => s == "alpine"),
            It.Is<string>(s => s == "3.11"),
            It.IsAny<IProgress<ImagePullProgress>>(),
            It.IsAny<CancellationToken>()), Times.Once);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task PullImageAsync_PullFailure_ThrowsDriverException()
    {
      var mockPack = new MockDriverPack();
      mockPack.ImageDriver
          .Setup(d => d.PullAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(), It.IsAny<string>(),
              It.IsAny<IProgress<ImagePullProgress>>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Fail("pull failed"));

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = new HostService(kernel, "docker", "test-host");
        var ex = await Assert.ThrowsAsync<DriverException>(
            () => service.PullImageAsync(
                "badimage",
                cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("pull image", ex.Message.ToLowerInvariant());
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task PullImageAsync_InspectFailure_ThrowsDriverException()
    {
      var mockPack = new MockDriverPack();
      mockPack.SetupImagePull();
      mockPack.ImageDriver
          .Setup(d => d.InspectAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Image>.Fail("inspect failed"));

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = new HostService(kernel, "docker", "test-host");
        var ex = await Assert.ThrowsAsync<DriverException>(
            () => service.PullImageAsync(
                "nginx",
                cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("inspect", ex.Message.ToLowerInvariant());
      }
      finally { kernel.Dispose(); }
    }

    #endregion

    #region Image Management -- BuildImageAsync

    [Fact]
    public async Task BuildImageAsync_Success_ReturnsImageService()
    {
      var mockPack = new MockDriverPack();
      mockPack.ImageDriver
          .Setup(d => d.BuildAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<ImageBuildConfig>(),
              It.IsAny<IProgress<ImageBuildProgress>>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<ImageBuildResult>.Ok(
              new ImageBuildResult { ImageId = "sha256:built999" }));

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = new HostService(kernel, "docker", "test-host");
        var config = new ImageBuildConfig
        {
          BuildContext = "/tmp/build",
          Tags = new List<string> { "myapp:v1.0" }
        };
        var image = await service.BuildImageAsync(
            config, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(image);
        mockPack.ImageDriver.Verify(d => d.BuildAsync(
            It.IsAny<DriverContext>(),
            It.Is<ImageBuildConfig>(c => c.BuildContext == "/tmp/build"),
            It.IsAny<IProgress<ImageBuildProgress>>(),
            It.IsAny<CancellationToken>()), Times.Once);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task BuildImageAsync_NoTags_HandlesGracefully()
    {
      var mockPack = new MockDriverPack();
      mockPack.ImageDriver
          .Setup(d => d.BuildAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<ImageBuildConfig>(),
              It.IsAny<IProgress<ImageBuildProgress>>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<ImageBuildResult>.Ok(
              new ImageBuildResult { ImageId = "sha256:notag" }));

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = new HostService(kernel, "docker", "test-host");
        var config = new ImageBuildConfig { BuildContext = "/tmp/build" };
        var image = await service.BuildImageAsync(
            config, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(image);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task BuildImageAsync_Failure_ThrowsDriverException()
    {
      var mockPack = new MockDriverPack();
      mockPack.ImageDriver
          .Setup(d => d.BuildAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<ImageBuildConfig>(),
              It.IsAny<IProgress<ImageBuildProgress>>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<ImageBuildResult>.Fail("build failed"));

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = new HostService(kernel, "docker", "test-host");
        var config = new ImageBuildConfig { BuildContext = "/tmp/build" };
        var ex = await Assert.ThrowsAsync<DriverException>(
            () => service.BuildImageAsync(
                config, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("build image", ex.Message.ToLowerInvariant());
      }
      finally { kernel.Dispose(); }
    }

    #endregion

    #region Container Management -- Extended

    [Fact]
    public async Task CreateContainerAsync_Failure_ThrowsDriverException()
    {
      var mockPack = new MockDriverPack();
      mockPack.ContainerDriver
          .Setup(d => d.CreateAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<ContainerCreateConfig>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<ContainerCreateResult>.Fail("create failed"));

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = new HostService(kernel, "docker", "test-host");
        var ex = await Assert.ThrowsAsync<DriverException>(
            () => service.CreateContainerAsync(
                "nginx:latest",
                cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("create container", ex.Message.ToLowerInvariant());
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task CreateContainerAsync_WithForcePull_PullsFirst()
    {
      var mockPack = new MockDriverPack();
      mockPack.SetupImagePull();
      mockPack.SetupContainerCreate("fp-container-123");

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = new HostService(kernel, "docker", "test-host");
        var config = new ContainerCreateOptions { ForcePull = true };
        var container = await service.CreateContainerAsync(
            "nginx:latest", config,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(container);
        mockPack.ImageDriver.Verify(d => d.PullAsync(
            It.IsAny<DriverContext>(),
            It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<IProgress<ImagePullProgress>>(),
            It.IsAny<CancellationToken>()), Times.Once);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task CreateContainerAsync_ForcePullFails_ThrowsDriverException()
    {
      var mockPack = new MockDriverPack();
      mockPack.ImageDriver
          .Setup(d => d.PullAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(), It.IsAny<string>(),
              It.IsAny<IProgress<ImagePullProgress>>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Fail("pull failed"));

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = new HostService(kernel, "docker", "test-host");
        var config = new ContainerCreateOptions { ForcePull = true };
        var ex = await Assert.ThrowsAsync<DriverException>(
            () => service.CreateContainerAsync(
                "nginx:latest", config,
                cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("pull image", ex.Message.ToLowerInvariant());
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task CreateContainerAsync_FullConfig_MapsAllOptions()
    {
      ContainerCreateConfig capturedConfig = null;
      var mockPack = new MockDriverPack();
      mockPack.ContainerDriver
          .Setup(d => d.CreateAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<ContainerCreateConfig>(),
              It.IsAny<CancellationToken>()))
          .Callback<DriverContext, ContainerCreateConfig, CancellationToken>(
              (_, cfg, _) => capturedConfig = cfg)
          .ReturnsAsync(CommandResponse<ContainerCreateResult>.Ok(
              new ContainerCreateResult { Id = "full-config-ctr" }));

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = new HostService(kernel, "docker", "test-host");
        var config = new ContainerCreateOptions
        {
          Name = "my-container",
          Command = new[] { "/bin/sh", "-c", "echo hello" },
          WorkingDir = "/app",
          User = "appuser",
          Privileged = true,
          Network = "my-net",
          MemoryLimit = 536870912,
          CpuQuota = 50000
        };
        await service.CreateContainerAsync(
            "alpine:latest", config,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(capturedConfig);
        Assert.Equal("alpine:latest", capturedConfig.Image);
        Assert.Equal("my-container", capturedConfig.Name);
        Assert.Equal("/app", capturedConfig.WorkingDirectory);
        Assert.Equal("appuser", capturedConfig.User);
        Assert.True(capturedConfig.Privileged);
        Assert.Equal("my-net", capturedConfig.NetworkMode);
        Assert.Equal(536870912, capturedConfig.MemoryLimit);
        Assert.Equal(50000, capturedConfig.CpuShares);
      }
      finally { kernel.Dispose(); }
    }

    #endregion
  }
}
