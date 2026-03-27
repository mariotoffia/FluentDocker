using System;
using System.Collections.Generic;
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

#pragma warning disable CS0618 // IService obsolete — intentional test usage

namespace FluentDocker.Tests.CoreTests.Service
{
  [Trait("Category", "Unit")]
  public class HostServiceTests
  {
    #region Constructor Tests

    [Fact]
    public async Task Constructor_SetsProperties()
    {
      var mockPack = new MockDriverPack();
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = new HostService(kernel, "docker", "my-host", isNative: false, requireTls: true);
        Assert.Equal("my-host", service.Name);
        Assert.Equal(ServiceRunningState.Running, service.State);
        Assert.Same(kernel, service.Kernel);
        Assert.Equal("docker", service.DriverId);
        Assert.False(service.IsNative);
        Assert.True(service.RequireTls);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public void Constructor_NullKernel_ThrowsArgumentNullException()
    {
      Assert.Throws<ArgumentNullException>(() =>
          new HostService(null, "docker", "test-host"));
    }

    [Fact]
    public void Constructor_NullDriverId_ThrowsArgumentNullException()
    {
      var kernel = new FluentDockerKernel();
      try
      {
        Assert.Throws<ArgumentNullException>(() =>
            new HostService(kernel, null, "test-host"));
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task Constructor_NullHostName_DefaultsToNative()
    {
      var mockPack = new MockDriverPack();
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = new HostService(kernel, "docker", null);
        Assert.Equal("native", service.Name);
      }
      finally { kernel.Dispose(); }
    }

    #endregion

    #region State Tests

    [Fact]
    public async Task State_IsAlwaysRunning()
    {
      var mockPack = new MockDriverPack();
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      try
      {
        var service = new HostService(kernel, "docker", "test-host");
        Assert.Equal(ServiceRunningState.Running, service.State);
      }
      finally { kernel.Dispose(); }
    }

    #endregion

    #region System Operation Tests

    [Fact]
    public async Task GetSystemInfoAsync_Success_ReturnsSystemInfo()
    {
      var expectedInfo = new SystemInfo();
      var mockPack = new MockDriverPack();
      mockPack.SetupSystemInfo(expectedInfo);
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      var service = new HostService(kernel, "docker", "test-host");
      try
      {
        var result = await service.GetSystemInfoAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Same(expectedInfo, result);
        mockPack.SystemDriver.Verify(d => d.GetInfoAsync(
            It.IsAny<DriverContext>(),
            It.IsAny<System.Threading.CancellationToken>()), Times.Once);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task GetSystemInfoAsync_Failure_ThrowsDriverException()
    {
      var mockPack = new MockDriverPack();
      mockPack.SetupSystemInfoFailure("system info failed");
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      var service = new HostService(kernel, "docker", "test-host");
      try
      {
        var ex = await Assert.ThrowsAsync<DriverException>(
            () => service.GetSystemInfoAsync(TestContext.Current.CancellationToken));
        Assert.Contains("system info", ex.Message);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task GetVersionAsync_Success_ReturnsVersionInfo()
    {
      var expectedInfo = new VersionInfo();
      var mockPack = new MockDriverPack();
      mockPack.SetupSystemVersion(expectedInfo);
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      var service = new HostService(kernel, "docker", "test-host");
      try
      {
        var result = await service.GetVersionAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Same(expectedInfo, result);
        mockPack.SystemDriver.Verify(d => d.GetVersionAsync(
            It.IsAny<DriverContext>(),
            It.IsAny<System.Threading.CancellationToken>()), Times.Once);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task GetVersionAsync_Failure_ThrowsDriverException()
    {
      var mockPack = new MockDriverPack();
      mockPack.SetupSystemVersionFailure("version failed");
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      var service = new HostService(kernel, "docker", "test-host");
      try
      {
        var ex = await Assert.ThrowsAsync<DriverException>(
            () => service.GetVersionAsync(TestContext.Current.CancellationToken));
        Assert.Contains("version info", ex.Message);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task PingAsync_Success_ReturnsTrue()
    {
      var mockPack = new MockDriverPack();
      mockPack.SetupSystemPing(true);
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      var service = new HostService(kernel, "docker", "test-host");
      try
      {
        var result = await service.PingAsync(TestContext.Current.CancellationToken);
        Assert.True(result);
        mockPack.SystemDriver.Verify(d => d.PingAsync(
            It.IsAny<DriverContext>(),
            It.IsAny<System.Threading.CancellationToken>()), Times.Once);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task PingAsync_Failure_ReturnsFalse()
    {
      var mockPack = new MockDriverPack();
      mockPack.SetupSystemPing(false);
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      var service = new HostService(kernel, "docker", "test-host");
      try
      {
        var result = await service.PingAsync(TestContext.Current.CancellationToken);
        Assert.False(result);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task GetDiskUsageAsync_Success_ReturnsDiskUsageInfo()
    {
      var expectedInfo = new DiskUsageInfo();
      var mockPack = new MockDriverPack();
      mockPack.SetupSystemDiskUsage(expectedInfo);
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      var service = new HostService(kernel, "docker", "test-host");
      try
      {
        var result = await service.GetDiskUsageAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Same(expectedInfo, result);
        mockPack.SystemDriver.Verify(d => d.GetDiskUsageAsync(
            It.IsAny<DriverContext>(),
            It.IsAny<System.Threading.CancellationToken>()), Times.Once);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task GetDiskUsageAsync_Failure_ThrowsDriverException()
    {
      var mockPack = new MockDriverPack();
      mockPack.SetupSystemDiskUsageFailure("disk usage failed");
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      var service = new HostService(kernel, "docker", "test-host");
      try
      {
        var ex = await Assert.ThrowsAsync<DriverException>(
            () => service.GetDiskUsageAsync(TestContext.Current.CancellationToken));
        Assert.Contains("disk usage", ex.Message);
      }
      finally { kernel.Dispose(); }
    }

    #endregion

    #region Container Management Tests

    [Fact]
    public async Task GetContainersAsync_ReturnsContainerServiceList()
    {
      var mockPack = new MockDriverPack();
      mockPack.SetupContainerList(
          new Container { Id = "c1", Image = "nginx:latest", Name = "web" },
          new Container { Id = "c2", Image = "redis:7", Name = "cache" });
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      var service = new HostService(kernel, "docker", "test-host");
      try
      {
        var containers = await service.GetContainersAsync(
            all: true, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(containers);
        Assert.Equal(2, containers.Count);
        mockPack.ContainerDriver.Verify(d => d.ListAsync(
            It.IsAny<DriverContext>(),
            It.IsAny<ContainerListFilter>(),
            It.IsAny<System.Threading.CancellationToken>()), Times.Once);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task CreateContainerAsync_Success_ReturnsContainerService()
    {
      var mockPack = new MockDriverPack();
      mockPack.SetupContainerCreate("new-container-456");
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      var service = new HostService(kernel, "docker", "test-host");
      try
      {
        var container = await service.CreateContainerAsync(
            "nginx:latest", cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(container);
        mockPack.ContainerDriver.Verify(d => d.CreateAsync(
            It.IsAny<DriverContext>(),
            It.IsAny<ContainerCreateConfig>(),
            It.IsAny<System.Threading.CancellationToken>()), Times.Once);
      }
      finally { kernel.Dispose(); }
    }

    #endregion

    #region Unsupported Operation Tests

    [Fact]
    public async Task PauseAsync_ThrowsNotSupportedException()
    {
      var mockPack = new MockDriverPack();
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      var service = new HostService(kernel, "docker", "test-host");
      try
      {
        await Assert.ThrowsAsync<NotSupportedException>(
            () => service.PauseAsync(TestContext.Current.CancellationToken));
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task StopAsync_ThrowsNotSupportedException()
    {
      var mockPack = new MockDriverPack();
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      var service = new HostService(kernel, "docker", "test-host");
      try
      {
        await Assert.ThrowsAsync<NotSupportedException>(
            () => service.StopAsync(TestContext.Current.CancellationToken));
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task RemoveAsync_ThrowsNotSupportedException()
    {
      var mockPack = new MockDriverPack();
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      var service = new HostService(kernel, "docker", "test-host");
      try
      {
        await Assert.ThrowsAsync<NotSupportedException>(
            () => service.RemoveAsync(cancellationToken: TestContext.Current.CancellationToken));
      }
      finally { kernel.Dispose(); }
    }

    #endregion

    #region Hook Tests

    [Fact]
    public async Task AddHook_StoresHook_RemoveHook_RemovesHook()
    {
      var mockPack = new MockDriverPack();
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      var service = new HostService(kernel, "docker", "test-host");
      try
      {
        // Add hook — verify fluent return
        var addResult = service.AddHook(
            ServiceRunningState.Running, _ => Task.CompletedTask, "test-hook");
        Assert.Same(service, addResult);

        // Remove hook — verify fluent return
        var removeResult = service.RemoveHook("test-hook");
        Assert.Same(service, removeResult);
      }
      finally { kernel.Dispose(); }
    }

    #endregion

    #region StartAsync Tests

    [Fact]
    public async Task StartAsync_CompletesSuccessfully()
    {
      var mockPack = new MockDriverPack();
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);
      var service = new HostService(kernel, "docker", "test-host");
      try
      {
        // Should complete without throwing
        await service.StartAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ServiceRunningState.Running, service.State);
      }
      finally { kernel.Dispose(); }
    }

    #endregion
  }
}
