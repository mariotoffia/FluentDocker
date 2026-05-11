using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Containers;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Volumes;
using FluentDocker.Services;
using FluentDocker.Services.Impl;
using FluentDocker.Tests.Mocks;
using Moq;
using Xunit;


namespace FluentDocker.Tests.CoreTests.Service
{
  /// <summary>
  /// Tests for HostService network, volume, maintenance, dispose,
  /// and IServiceCapabilities operations.
  /// </summary>
  [Trait("Category", "Unit")]
  public class HostServiceResourceTests
  {
    #region Network Management

    [Fact]
    public async Task GetNetworksAsync_Success_ReturnsNetworkServices()
    {
      var mockPack = new MockDriverPack();
      mockPack.SetupNetworkList(
          new Network { Id = "net1", Name = "bridge" },
          new Network { Id = "net2", Name = "custom-net" });

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = new HostService(kernel, "docker", "test-host");
        var networks = await service.GetNetworksAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, networks.Count);
        mockPack.NetworkDriver.Verify(d => d.ListAsync(
            It.IsAny<DriverContext>(),
            It.IsAny<NetworkListFilter>(),
            It.IsAny<CancellationToken>()), Times.Once);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task GetNetworksAsync_Failure_ThrowsDriverException()
    {
      var mockPack = new MockDriverPack();
      mockPack.NetworkDriver
          .Setup(d => d.ListAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<NetworkListFilter>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<IList<Network>>.Fail("list networks failed"));

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = new HostService(kernel, "docker", "test-host");
        var ex = await Assert.ThrowsAsync<DriverException>(
            () => service.GetNetworksAsync(
                cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("list networks", ex.Message.ToLowerInvariant());
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task CreateNetworkAsync_Success_ReturnsNetworkService()
    {
      var mockPack = new MockDriverPack();
      mockPack.SetupNetworkCreate("new-net-id");

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = new HostService(kernel, "docker", "test-host");
        var network = await service.CreateNetworkAsync(
            "my-network",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(network);
        Assert.Equal("my-network", network.Name);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task CreateNetworkAsync_Failure_ThrowsDriverException()
    {
      var mockPack = new MockDriverPack();
      mockPack.NetworkDriver
          .Setup(d => d.CreateAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<Drivers.NetworkCreateConfig>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<NetworkCreateResult>.Fail("create network failed"));

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = new HostService(kernel, "docker", "test-host");
        var ex = await Assert.ThrowsAsync<DriverException>(
            () => service.CreateNetworkAsync(
                "bad-net",
                cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("create network", ex.Message.ToLowerInvariant());
      }
      finally { kernel.Dispose(); }
    }

    #endregion

    #region Volume Management

    [Fact]
    public async Task GetVolumesAsync_Success_ReturnsVolumeServices()
    {
      var mockPack = new MockDriverPack();
      mockPack.VolumeDriver
          .Setup(d => d.ListAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<VolumeListFilter>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<IList<Volume>>.Ok(
          [
            new Volume { Name = "vol1", Driver = "local" },
            new Volume { Name = "vol2", Driver = "local" }
          ]));

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = new HostService(kernel, "docker", "test-host");
        var volumes = await service.GetVolumesAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, volumes.Count);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task GetVolumesAsync_Failure_ThrowsDriverException()
    {
      var mockPack = new MockDriverPack();
      mockPack.VolumeDriver
          .Setup(d => d.ListAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<VolumeListFilter>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<IList<Volume>>.Fail("list volumes failed"));

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = new HostService(kernel, "docker", "test-host");
        var ex = await Assert.ThrowsAsync<DriverException>(
            () => service.GetVolumesAsync(
                cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("list volumes", ex.Message.ToLowerInvariant());
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task CreateVolumeAsync_Success_ReturnsVolumeService()
    {
      var mockPack = new MockDriverPack();
      mockPack.SetupVolumeCreate("my-vol");

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = new HostService(kernel, "docker", "test-host");
        var volume = await service.CreateVolumeAsync(
            "my-vol",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(volume);
        Assert.Equal("my-vol", volume.Name);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task CreateVolumeAsync_Failure_ThrowsDriverException()
    {
      var mockPack = new MockDriverPack();
      mockPack.VolumeDriver
          .Setup(d => d.CreateAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<VolumeCreateConfig>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<VolumeCreateResult>.Fail("create volume failed"));

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = new HostService(kernel, "docker", "test-host");
        var ex = await Assert.ThrowsAsync<DriverException>(
            () => service.CreateVolumeAsync(
                "bad-vol",
                cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("create volume", ex.Message.ToLowerInvariant());
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task CreateVolumeAsync_WithLabelsAndOptions_PassesConfig()
    {
      VolumeCreateConfig capturedConfig = null;
      var mockPack = new MockDriverPack();
      mockPack.VolumeDriver
          .Setup(d => d.CreateAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<VolumeCreateConfig>(),
              It.IsAny<CancellationToken>()))
          .Callback<DriverContext, VolumeCreateConfig, CancellationToken>(
              (_, cfg, _) => capturedConfig = cfg)
          .ReturnsAsync(CommandResponse<VolumeCreateResult>.Ok(
              new VolumeCreateResult { Name = "labeled-vol", Driver = "local" }));

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = new HostService(kernel, "docker", "test-host");
        var labels = new Dictionary<string, string> { { "env", "prod" } };
        var options = new Dictionary<string, string> { { "type", "nfs" } };

        await service.CreateVolumeAsync(
            "labeled-vol", "local", labels, options,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(capturedConfig);
        Assert.Equal("labeled-vol", capturedConfig.Name);
        Assert.Equal("local", capturedConfig.Driver);
        Assert.Equal("prod", capturedConfig.Labels["env"]);
        Assert.Equal("nfs", capturedConfig.DriverOpts["type"]);
      }
      finally { kernel.Dispose(); }
    }

    #endregion

    #region Maintenance -- PruneAsync

    [Fact]
    public async Task PruneAsync_Success_ReturnsResult()
    {
      var expected = new SystemPruneResult
      {
        ContainersDeleted = ["c1"],
        ImagesDeleted = ["img1"]
      };
      var mockPack = new MockDriverPack();
      mockPack.SystemDriver
          .Setup(d => d.PruneAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<SystemPruneConfig>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<SystemPruneResult>.Ok(expected));

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = new HostService(kernel, "docker", "test-host");
        var result = await service.PruneAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Single(result.ContainersDeleted);
        Assert.Single(result.ImagesDeleted);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task PruneAsync_WithConfig_PassesConfig()
    {
      var mockPack = new MockDriverPack();
      mockPack.SystemDriver
          .Setup(d => d.PruneAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<SystemPruneConfig>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<SystemPruneResult>.Ok(new SystemPruneResult()));

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = new HostService(kernel, "docker", "test-host");
        var config = new SystemPruneConfig { All = true, Volumes = true };

        await service.PruneAsync(
            config, cancellationToken: TestContext.Current.CancellationToken);

        mockPack.SystemDriver.Verify(d => d.PruneAsync(
            It.IsAny<DriverContext>(),
            It.Is<SystemPruneConfig>(c => c.All && c.Volumes),
            It.IsAny<CancellationToken>()), Times.Once);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task PruneAsync_Failure_ThrowsDriverException()
    {
      var mockPack = new MockDriverPack();
      mockPack.SystemDriver
          .Setup(d => d.PruneAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<SystemPruneConfig>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<SystemPruneResult>.Fail("prune failed"));

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = new HostService(kernel, "docker", "test-host");
        var ex = await Assert.ThrowsAsync<DriverException>(
            () => service.PruneAsync(
                cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("prune", ex.Message.ToLowerInvariant());
      }
      finally { kernel.Dispose(); }
    }

    #endregion

    #region IServiceCapabilities

    [Fact]
    public async Task ServiceCapabilities_ReportsCorrectValues()
    {
      var mockPack = new MockDriverPack();
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = new HostService(kernel, "docker", "test-host");
        var caps = (IServiceCapabilities)service;

        Assert.True(caps.CanStart);
        Assert.True(caps.CanStop);
        Assert.False(caps.CanPause);
        Assert.True(caps.CanRemove);
      }
      finally { kernel.Dispose(); }
    }

    #endregion

    #region DisposeAsync

    [Fact]
    public async Task DisposeAsync_CanBeCalledMultipleTimes()
    {
      var mockPack = new MockDriverPack();
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = new HostService(kernel, "docker", "test-host");
        await service.DisposeAsync();
        await service.DisposeAsync(); // no-op
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task Dispose_Sync_CanBeCalledMultipleTimes()
    {
      var mockPack = new MockDriverPack();
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = new HostService(kernel, "docker", "test-host");
        service.Dispose();
        service.Dispose(); // no-op
      }
      finally { kernel.Dispose(); }
    }

    #endregion

    #region GetRunningContainersAsync

    [Fact]
    public async Task GetRunningContainersAsync_CallsGetContainersWithAllFalse()
    {
      ContainerListFilter capturedFilter = null;
      var mockPack = new MockDriverPack();
      mockPack.ContainerDriver
          .Setup(d => d.ListAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<ContainerListFilter>(),
              It.IsAny<CancellationToken>()))
          .Callback<DriverContext, ContainerListFilter, CancellationToken>(
              (_, f, _) => capturedFilter = f)
          .ReturnsAsync(CommandResponse<IList<Container>>.Ok(
          [
            new Container { Id = "run1", Image = "nginx", Name = "web" }
          ]));

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = new HostService(kernel, "docker", "test-host");
        var containers = await service.GetRunningContainersAsync(
            TestContext.Current.CancellationToken);

        Assert.Single(containers);
        Assert.NotNull(capturedFilter);
        Assert.False(capturedFilter.All);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task GetContainersAsync_Failure_ThrowsDriverException()
    {
      var mockPack = new MockDriverPack();
      mockPack.ContainerDriver
          .Setup(d => d.ListAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<ContainerListFilter>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<IList<Container>>.Fail("list failed"));

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = new HostService(kernel, "docker", "test-host");
        var ex = await Assert.ThrowsAsync<DriverException>(
            () => service.GetContainersAsync(
                cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("list containers", ex.Message.ToLowerInvariant());
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task GetContainersAsync_WithFilters_PassesLabels()
    {
      ContainerListFilter capturedFilter = null;
      var mockPack = new MockDriverPack();
      mockPack.ContainerDriver
          .Setup(d => d.ListAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<ContainerListFilter>(),
              It.IsAny<CancellationToken>()))
          .Callback<DriverContext, ContainerListFilter, CancellationToken>(
              (_, f, _) => capturedFilter = f)
          .ReturnsAsync(CommandResponse<IList<Container>>.Ok([]));

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = new HostService(kernel, "docker", "test-host");
        var filters = new Dictionary<string, string> { { "env", "test" } };
        await service.GetContainersAsync(
            all: true, filters: filters,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(capturedFilter);
        Assert.True(capturedFilter.All);
        Assert.Equal("test", capturedFilter.Labels["env"]);
      }
      finally { kernel.Dispose(); }
    }

    #endregion
  }
}
