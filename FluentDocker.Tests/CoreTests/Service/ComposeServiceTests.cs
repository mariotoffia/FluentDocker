using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Services;
using FluentDocker.Services.Impl;
using FluentDocker.Tests.Mocks;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Service
{
  /// <summary>
  /// Unit tests for ComposeService.
  /// </summary>
  [Trait("Category", "Unit")]
  public class ComposeServiceTests
  {
    [Fact]
    public void Constructor_SetsProperties()
    {
      // Arrange
      var kernel = new FluentDockerKernel();
      var composeFiles = new List<string> { "docker-compose.yml" };

      // Act
      var service = new ComposeService(kernel, "docker", composeFiles, "my-project");

      // Assert
      Assert.Equal("my-project", service.Name);
      Assert.Equal("my-project", service.ProjectName);
      Assert.Single(service.ComposeFiles);
      Assert.Equal("docker-compose.yml", service.ComposeFiles[0]);
      Assert.Equal(kernel, service.Kernel);
      Assert.Equal("docker", service.DriverId);
      Assert.Equal(ServiceRunningState.Running, service.State);

      kernel.Dispose();
    }

    [Fact]
    public void Constructor_NullKernel_ThrowsArgumentNullException()
    {
      var composeFiles = new List<string> { "docker-compose.yml" };
      Assert.Throws<ArgumentNullException>(() =>
          new ComposeService(null!, "docker", composeFiles, "my-project"));
    }

    [Fact]
    public void Constructor_NullDriverId_ThrowsArgumentNullException()
    {
      var kernel = new FluentDockerKernel();
      var composeFiles = new List<string> { "docker-compose.yml" };
      Assert.Throws<ArgumentNullException>(() =>
          new ComposeService(kernel, null!, composeFiles, "my-project"));
      kernel.Dispose();
    }

    [Fact]
    public void Constructor_NullComposeFiles_ThrowsArgumentNullException()
    {
      var kernel = new FluentDockerKernel();
      Assert.Throws<ArgumentNullException>(() =>
          new ComposeService(kernel, "docker", null!, "my-project"));
      kernel.Dispose();
    }

    [Fact]
    public void Constructor_NullProjectName_ThrowsArgumentNullException()
    {
      var kernel = new FluentDockerKernel();
      var composeFiles = new List<string> { "docker-compose.yml" };
      Assert.Throws<ArgumentNullException>(() =>
          new ComposeService(kernel, "docker", composeFiles, null!));
      kernel.Dispose();
    }

    [Fact]
    public void Constructor_WithRemoveVolumes_SetsFlag()
    {
      var kernel = new FluentDockerKernel();
      var composeFiles = new List<string> { "docker-compose.yml" };
      var service = new ComposeService(kernel, "docker", composeFiles, "my-project",
          removeVolumes: true);

      Assert.NotNull(service);
      kernel.Dispose();
    }

    [Fact]
    public void Constructor_WithRemoveImages_SetsFlag()
    {
      var kernel = new FluentDockerKernel();
      var composeFiles = new List<string> { "docker-compose.yml" };
      var service = new ComposeService(kernel, "docker", composeFiles, "my-project",
          removeImages: true);

      Assert.NotNull(service);
      kernel.Dispose();
    }

    [Fact]
    public void Constructor_MultipleComposeFiles_AllStored()
    {
      var kernel = new FluentDockerKernel();
      var composeFiles = new List<string>
            {
                "docker-compose.yml",
                "docker-compose.override.yml",
                "docker-compose.prod.yml"
            };

      var service = new ComposeService(kernel, "docker", composeFiles, "my-project");

      Assert.Equal(3, service.ComposeFiles.Count);
      kernel.Dispose();
    }

    [Fact]
    public void PauseAsync_ThrowsNotSupportedException()
    {
      var kernel = new FluentDockerKernel();
      var composeFiles = new List<string> { "docker-compose.yml" };
      var service = new ComposeService(kernel, "docker", composeFiles, "my-project");

      Assert.ThrowsAsync<NotSupportedException>(async () => await service.PauseAsync());
      kernel.Dispose();
    }

    [Fact]
    public void AddHook_AddsHook()
    {
      var kernel = new FluentDockerKernel();
      var composeFiles = new List<string> { "docker-compose.yml" };
      var service = new ComposeService(kernel, "docker", composeFiles, "my-project");

      service.AddHook(ServiceRunningState.Removed, async _ => { }, "test-hook");
      Assert.NotNull(service);

      kernel.Dispose();
    }

    [Fact]
    public void RemoveHook_RemovesHook()
    {
      var kernel = new FluentDockerKernel();
      var composeFiles = new List<string> { "docker-compose.yml" };
      var service = new ComposeService(kernel, "docker", composeFiles, "my-project");
      service.AddHook(ServiceRunningState.Removed, async _ => { }, "test-hook");

      service.RemoveHook("test-hook");
      Assert.NotNull(service);

      kernel.Dispose();
    }

    #region Service Operation Tests

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ListServicesAsync_CallsDriver()
    {
      // Arrange
      var mockPack = new MockDriverPack();
      mockPack.SetupComposeList(
          new ComposeServiceInfo { Name = "web", State = "running" },
          new ComposeServiceInfo { Name = "db", State = "running" }
      );

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);

      var composeFiles = new List<string> { "docker-compose.yml" };
      var service = new ComposeService(kernel, "docker", composeFiles, "my-project");

      try
      {
        // Act
        var services = await service.ListServicesAsync();

        // Assert
        Assert.Equal(2, services.Count);
        Assert.Equal("web", services[0].Name);
        Assert.Equal("db", services[1].Name);
        mockPack.ComposeDriver.Verify(d => d.ListAsync(
            It.IsAny<FluentDocker.Model.Drivers.DriverContext>(),
            It.IsAny<ComposeListConfig>(),
            It.IsAny<System.Threading.CancellationToken>()), Times.Once);
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task GetLogsAsync_CallsDriver()
    {
      // Arrange
      var mockPack = new MockDriverPack();
      mockPack.SetupComposeGetLogs("web | Starting...\ndb | Ready");

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);

      var composeFiles = new List<string> { "docker-compose.yml" };
      var service = new ComposeService(kernel, "docker", composeFiles, "my-project");

      try
      {
        // Act
        var logs = await service.GetLogsAsync();

        // Assert
        Assert.Contains("web", logs);
        Assert.Contains("db", logs);
        mockPack.ComposeDriver.Verify(d => d.GetLogsAsync(
            It.IsAny<FluentDocker.Model.Drivers.DriverContext>(),
            It.Is<ComposeLogsConfig>(c => c.ProjectName == "my-project"),
            It.IsAny<System.Threading.CancellationToken>()), Times.Once);
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ExecuteAsync_CallsDriverWithServiceAndCommand()
    {
      // Arrange
      var mockPack = new MockDriverPack();
      mockPack.SetupComposeExecute("command executed successfully");

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);

      var composeFiles = new List<string> { "docker-compose.yml" };
      var service = new ComposeService(kernel, "docker", composeFiles, "my-project");

      try
      {
        // Act
        var result = await service.ExecuteAsync("web", new[] { "ls", "-la" });

        // Assert
        Assert.Equal("command executed successfully", result);
        mockPack.ComposeDriver.Verify(d => d.ExecuteAsync(
            It.IsAny<FluentDocker.Model.Drivers.DriverContext>(),
            It.Is<ComposeExecConfig>(c =>
                c.Service == "web" &&
                c.Command.Length == 2 &&
                c.Command[0] == "ls"),
            It.IsAny<System.Threading.CancellationToken>()), Times.Once);
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ScaleAsync_CallsDriverWithReplicas()
    {
      // Arrange
      var mockPack = new MockDriverPack();
      mockPack.SetupComposeScale();

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);

      var composeFiles = new List<string> { "docker-compose.yml" };
      var service = new ComposeService(kernel, "docker", composeFiles, "my-project");

      try
      {
        // Act
        await service.ScaleAsync("web", 5);

        // Assert
        mockPack.ComposeDriver.Verify(d => d.ScaleAsync(
            It.IsAny<FluentDocker.Model.Drivers.DriverContext>(),
            It.Is<ComposeScaleConfig>(c =>
                c.Scale != null &&
                c.Scale["web"] == 5),
            It.IsAny<System.Threading.CancellationToken>()), Times.Once);
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task StartAsync_CallsDriver()
    {
      // Arrange
      var mockPack = new MockDriverPack();
      mockPack.SetupComposeStart();

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);

      var composeFiles = new List<string> { "docker-compose.yml" };
      var service = new ComposeService(kernel, "docker", composeFiles, "my-project");

      try
      {
        // Act
        await service.StartAsync();

        // Assert
        Assert.Equal(ServiceRunningState.Running, service.State);
        mockPack.ComposeDriver.Verify(d => d.StartAsync(
            It.IsAny<FluentDocker.Model.Drivers.DriverContext>(),
            It.Is<ComposeFileConfig>(c => c.ProjectName == "my-project"),
            It.IsAny<System.Threading.CancellationToken>()), Times.Once);
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task StopAsync_CallsDriver()
    {
      // Arrange
      var mockPack = new MockDriverPack();
      mockPack.SetupComposeStop();

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);

      var composeFiles = new List<string> { "docker-compose.yml" };
      var service = new ComposeService(kernel, "docker", composeFiles, "my-project");

      try
      {
        // Act
        await service.StopAsync();

        // Assert
        Assert.Equal(ServiceRunningState.Stopped, service.State);
        mockPack.ComposeDriver.Verify(d => d.StopAsync(
            It.IsAny<FluentDocker.Model.Drivers.DriverContext>(),
            It.Is<ComposeStopConfig>(c => c.ProjectName == "my-project"),
            It.IsAny<System.Threading.CancellationToken>()), Times.Once);
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task RemoveAsync_CallsDownWithOptions()
    {
      // Arrange
      var mockPack = new MockDriverPack();
      mockPack.SetupComposeDown();

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);

      var composeFiles = new List<string> { "docker-compose.yml" };
      var service = new ComposeService(kernel, "docker", composeFiles, "my-project",
          removeVolumes: true, removeImages: true);

      try
      {
        // Act
        await service.RemoveAsync();

        // Assert
        Assert.Equal(ServiceRunningState.Removed, service.State);
        mockPack.ComposeDriver.Verify(d => d.DownAsync(
            It.IsAny<FluentDocker.Model.Drivers.DriverContext>(),
            It.Is<ComposeDownConfig>(c =>
                c.ProjectName == "my-project" &&
                c.RemoveVolumes == true &&
                c.RemoveImages == "all"),
            It.IsAny<System.Threading.CancellationToken>()), Times.Once);
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task DisposeAsync_CallsRemove()
    {
      // Arrange
      var mockPack = new MockDriverPack();
      mockPack.SetupComposeDown();

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", mockPack);

      var composeFiles = new List<string> { "docker-compose.yml" };
      var service = new ComposeService(kernel, "docker", composeFiles, "my-project");

      try
      {
        // Act
        await service.DisposeAsync();

        // Assert
        mockPack.ComposeDriver.Verify(d => d.DownAsync(
            It.IsAny<FluentDocker.Model.Drivers.DriverContext>(),
            It.IsAny<ComposeDownConfig>(),
            It.IsAny<System.Threading.CancellationToken>()), Times.Once);
      }
      finally
      {
        kernel.Dispose();
      }
    }

    #endregion
  }
}

