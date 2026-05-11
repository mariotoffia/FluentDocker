using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Common;
using FluentDocker.Drivers.Podman;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using FluentDocker.Services;
using FluentDocker.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.BuilderTests
{
  /// <summary>
  /// Unit tests for the PodBuilder internal implementation.
  /// </summary>
  [Trait("Category", "Unit")]
  public class PodBuilderTests
  {
    [Fact]
    public void PodBuilder_ImplementsIDriverScopedBuilder()
    {
      var builderType = typeof(Builder).Assembly
          .GetType("FluentDocker.Builders.PodBuilder");
      Assert.NotNull(builderType);
      Assert.True(typeof(IDriverScopedBuilder).IsAssignableFrom(builderType));
    }

    [Fact]
    public void PodBuilder_ImplementsIPodBuilder()
    {
      var builderType = typeof(Builder).Assembly
          .GetType("FluentDocker.Builders.PodBuilder");
      Assert.NotNull(builderType);
      Assert.True(typeof(IPodBuilder).IsAssignableFrom(builderType));
    }

    [Fact]
    public void PodBuilder_WithName_ChainsCorrectly()
    {
      var builder = CreatePodBuilder("podman");
      var result = builder.WithName("test-pod");
      Assert.Same(builder, result);
    }

    [Fact]
    public void PodBuilder_WithPort_ChainsCorrectly()
    {
      var builder = CreatePodBuilder("podman");
      var result = builder.WithPort("8080", "80");
      Assert.Same(builder, result);
    }

    [Fact]
    public void PodBuilder_ExposePort_ChainsCorrectly()
    {
      var builder = CreatePodBuilder("podman");
      var result = builder.ExposePort("3000");
      Assert.Same(builder, result);
    }

    [Fact]
    public void PodBuilder_WithNetwork_ChainsCorrectly()
    {
      var builder = CreatePodBuilder("podman");
      var result = builder.WithNetwork("pod-net");
      Assert.Same(builder, result);
    }

    [Fact]
    public void PodBuilder_WithLabel_ChainsCorrectly()
    {
      var builder = CreatePodBuilder("podman");
      var result = builder.WithLabel("env", "test");
      Assert.Same(builder, result);
    }

    [Fact]
    public void PodBuilder_WithHostname_ChainsCorrectly()
    {
      var builder = CreatePodBuilder("podman");
      var result = builder.WithHostname("my-host");
      Assert.Same(builder, result);
    }

    [Fact]
    public void PodBuilder_RemoveOnDispose_ChainsCorrectly()
    {
      var builder = CreatePodBuilder("podman");
      var result = builder.RemoveOnDispose();
      Assert.Same(builder, result);
    }

    [Fact]
    public void PodBuilder_FullChain_Works()
    {
      var builder = CreatePodBuilder("podman");
      var result = builder
          .WithName("full-test")
          .WithPort("8080", "80")
          .ExposePort("3000")
          .WithNetwork("pod-net")
          .WithLabel("env", "dev")
          .WithHostname("pod-host")
          .RemoveOnDispose();

      Assert.Same(builder, result);
    }

    [Fact]
    public async Task PodBuilder_ExecuteAsync_CallsDriver()
    {
      // Arrange
      var mockPodDriver = new Mock<IPodmanPodDriver>();
      mockPodDriver
          .Setup(d => d.CreatePodAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<PodCreateConfig>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<PodCreateResult>.Ok(
              new PodCreateResult { Id = "pod-123" }));
      mockPodDriver
          .Setup(d => d.StartPodAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));

      var mockPack = new MockDriverPack();
      mockPack.RegisterCustomDriver(mockPodDriver.Object);
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync(
          "podman", mockPack);

      try
      {
        var builder = CreatePodBuilder("podman", kernel);
        builder
            .WithName("test-pod")
            .WithPort("8080", "80")
            .WithNetwork("my-net")
            .WithLabel("app", "web")
            .WithHostname("web-pod");

        // Act
        var service = await InvokeExecuteAsync(builder);

        // Assert
        Assert.NotNull(service);
        Assert.IsAssignableFrom<IPodService>(service);
        var podService = (IPodService)service;
        Assert.Equal("pod-123", podService.Id);
        Assert.Equal("test-pod", podService.Name);
        Assert.Equal(ServiceRunningState.Running, podService.State);

        mockPodDriver.Verify(d => d.CreatePodAsync(
            It.IsAny<DriverContext>(),
            It.Is<PodCreateConfig>(c =>
                c.Name == "test-pod" &&
                c.Network == "my-net" &&
                c.Hostname == "web-pod" &&
                c.Ports.Count == 1 &&
                c.Ports[0] == "8080:80" &&
                c.Labels["app"] == "web"),
            It.IsAny<CancellationToken>()), Times.Once);
        mockPodDriver.Verify(d => d.StartPodAsync(
            It.IsAny<DriverContext>(),
            "test-pod",
            It.IsAny<CancellationToken>()), Times.Once);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task PodBuilder_ExecuteAsync_DriverFailure_ThrowsDriverException()
    {
      // Arrange
      var mockPodDriver = new Mock<IPodmanPodDriver>();
      mockPodDriver
          .Setup(d => d.CreatePodAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<PodCreateConfig>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<PodCreateResult>.Fail("pod creation failed"));

      var mockPack = new MockDriverPack();
      mockPack.RegisterCustomDriver(mockPodDriver.Object);
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync(
          "podman", mockPack);

      try
      {
        var builder = CreatePodBuilder("podman", kernel);
        builder.WithName("fail-pod");

        // Act & Assert
        await Assert.ThrowsAsync<DriverException>(
            () => InvokeExecuteAsync(builder));
      }
      finally { kernel.Dispose(); }
    }

    #region Helpers

    private static IPodBuilder CreatePodBuilder(
        string driverId, FluentDockerKernel? kernel = null)
    {
      kernel ??= new FluentDockerKernel(new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      var builderType = typeof(Builder).Assembly
          .GetType("FluentDocker.Builders.PodBuilder");
      return (IPodBuilder)Activator.CreateInstance(builderType, kernel, driverId);
    }

    private static async Task<IServiceAsync> InvokeExecuteAsync(IPodBuilder builder)
    {
      var builderType = builder.GetType();
      var executeMethod = builderType.GetMethod("ExecuteAsync",
          BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
      Assert.NotNull(executeMethod);

      var task = (Task<IServiceAsync>)executeMethod.Invoke(
          builder, [CancellationToken.None]);
      return await task;
    }

    #endregion
  }
}
