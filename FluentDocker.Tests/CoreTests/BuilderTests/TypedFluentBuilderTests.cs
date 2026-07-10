using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Podman;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using FluentDocker.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.BuilderTests
{
  /// <summary>
  /// Unit tests for the typed fluent builder wrappers (DockerCliFluentBuilder,
  /// DockerApiFluentBuilder, PodmanCliFluentBuilder) and PodBuilder.
  /// </summary>
  [Trait("Category", "Unit")]
  public class TypedFluentBuilderTests
  {
    #region WithinDockerCli

    [Fact]
    public async Task WithinDockerCli_ReturnsDockerCliFluentBuilder()
    {
      var (kernel, _) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");
      try
      {
        var result = new Builder().WithinDockerCli("docker", kernel);
        Assert.NotNull(result);
        Assert.IsType<DockerCliFluentBuilder>(result);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task WithinDockerCli_UseContainer_ReturnsDockerCliBuilder()
    {
      var (kernel, _) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");
      try
      {
        var result = new Builder()
            .WithinDockerCli("docker", kernel)
            .UseContainer(c => c.UseImage("alpine:latest").WithName("test"));

        Assert.NotNull(result);
        Assert.IsType<DockerCliFluentBuilder>(result);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task WithinDockerCli_UseNetwork_ReturnsDockerCliBuilder()
    {
      var (kernel, _) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");
      try
      {
        var result = new Builder()
            .WithinDockerCli("docker", kernel)
            .UseNetwork(n => n.WithName("test-net"));

        Assert.NotNull(result);
        Assert.IsType<DockerCliFluentBuilder>(result);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task WithinDockerCli_UseVolume_ReturnsDockerCliBuilder()
    {
      var (kernel, _) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");
      try
      {
        var result = new Builder()
            .WithinDockerCli("docker", kernel)
            .UseVolume(v => v.WithName("test-vol"));

        Assert.NotNull(result);
        Assert.IsType<DockerCliFluentBuilder>(result);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task WithinDockerCli_UseCompose_ReturnsDockerCliBuilder()
    {
      var (kernel, _) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");
      try
      {
        var result = new Builder()
            .WithinDockerCli("docker", kernel)
            .UseCompose(c => c.WithComposeFile("docker-compose.yml"));

        Assert.NotNull(result);
        Assert.IsType<DockerCliFluentBuilder>(result);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task WithinDockerCli_Chaining_Works()
    {
      var (kernel, _) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");
      try
      {
        var result = new Builder()
            .WithinDockerCli("docker", kernel)
            .UseNetwork(n => n.WithName("net"))
            .UseVolume(v => v.WithName("vol"))
            .UseContainer(c => c.UseImage("alpine").WithName("test"));

        Assert.NotNull(result);
        Assert.IsType<DockerCliFluentBuilder>(result);
      }
      finally { kernel.Dispose(); }
    }

    #endregion

    #region WithinDockerApi

    [Fact]
    public async Task WithinDockerApi_ReturnsDockerApiFluentBuilder()
    {
      var (kernel, _) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("api");
      try
      {
        var result = new Builder().WithinDockerApi("api", kernel);
        Assert.NotNull(result);
        Assert.IsType<DockerApiFluentBuilder>(result);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task WithinDockerApi_UseContainer_ReturnsDockerApiBuilder()
    {
      var (kernel, _) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("api");
      try
      {
        var result = new Builder()
            .WithinDockerApi("api", kernel)
            .UseContainer(c => c.UseImage("alpine:latest").WithName("test"));

        Assert.NotNull(result);
        Assert.IsType<DockerApiFluentBuilder>(result);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task WithinDockerApi_UseNetwork_ReturnsDockerApiBuilder()
    {
      var (kernel, _) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("api");
      try
      {
        var result = new Builder()
            .WithinDockerApi("api", kernel)
            .UseNetwork(n => n.WithName("api-net"));

        Assert.NotNull(result);
        Assert.IsType<DockerApiFluentBuilder>(result);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task WithinDockerApi_Chaining_Works()
    {
      var (kernel, _) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("api");
      try
      {
        var result = new Builder()
            .WithinDockerApi("api", kernel)
            .UseNetwork(n => n.WithName("net"))
            .UseContainer(c => c.UseImage("alpine").WithName("test"));

        Assert.NotNull(result);
        Assert.IsType<DockerApiFluentBuilder>(result);
      }
      finally { kernel.Dispose(); }
    }

    #endregion

    #region WithinPodmanCli

    [Fact]
    public async Task WithinPodmanCli_ReturnsPodmanCliFluentBuilder()
    {
      var kernel = await CreatePodmanCapableKernelAsync();
      try
      {
        var result = new Builder().WithinPodmanCli("podman", kernel);
        Assert.NotNull(result);
        Assert.IsType<PodmanCliFluentBuilder>(result);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task WithinPodmanCli_UseContainer_ReturnsPodmanCliBuilder()
    {
      var kernel = await CreatePodmanCapableKernelAsync();
      try
      {
        var result = new Builder()
            .WithinPodmanCli("podman", kernel)
            .UseContainer(c => c.UseImage("alpine:latest").WithName("test"));

        Assert.NotNull(result);
        Assert.IsType<PodmanCliFluentBuilder>(result);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task WithinPodmanCli_UsePod_ReturnsPodmanCliBuilder()
    {
      var kernel = await CreatePodmanCapableKernelAsync();
      try
      {
        var result = new Builder()
            .WithinPodmanCli("podman", kernel)
            .UsePod(p => p.WithName("my-pod"));

        Assert.NotNull(result);
        Assert.IsType<PodmanCliFluentBuilder>(result);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task WithinPodmanCli_Chaining_Works()
    {
      var kernel = await CreatePodmanCapableKernelAsync();
      try
      {
        var result = new Builder()
            .WithinPodmanCli("podman", kernel)
            .UseNetwork(n => n.WithName("pod-net"))
            .UsePod(p => p.WithName("my-pod"))
            .UseContainer(c => c.UseImage("alpine").WithName("test"));

        Assert.NotNull(result);
        Assert.IsType<PodmanCliFluentBuilder>(result);
      }
      finally { kernel.Dispose(); }
    }

    #endregion

    #region WithinDriver (backward compat)

    [Fact]
    public async Task WithinDriver_UseCompose_StillWorks()
    {
      var (kernel, _) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker");
      try
      {
        // UseCompose is still available on Builder (not on IBuilder interface)
        var builder = new Builder()
            .WithinDriver("docker", kernel)
            .UseCompose(c => c.WithComposeFile("docker-compose.yml"));

        Assert.NotNull(builder);
        Assert.IsType<Builder>(builder);
      }
      finally { kernel.Dispose(); }
    }

    [Fact]
    public async Task WithinDriver_UsePod_StillWorks()
    {
      var kernel = await CreatePodmanCapableKernelAsync();
      try
      {
        var builder = new Builder()
            .WithinDriver("podman", kernel)
            .UsePod(p => p.WithName("generic-pod"));

        Assert.NotNull(builder);
        Assert.IsType<Builder>(builder);
      }
      finally { kernel.Dispose(); }
    }

    #endregion

    #region Builder Scope Validation

    [Fact]
    public void WithinDockerCli_NullKernelOnFirst_Throws()
    {
      Assert.Throws<InvalidOperationException>(() =>
          new Builder().WithinDockerCli("docker"));
    }

    [Fact]
    public void WithinDockerApi_NullKernelOnFirst_Throws()
    {
      Assert.Throws<InvalidOperationException>(() =>
          new Builder().WithinDockerApi("api"));
    }

    [Fact]
    public void WithinPodmanCli_NullKernelOnFirst_Throws()
    {
      Assert.Throws<InvalidOperationException>(() =>
          new Builder().WithinPodmanCli("podman"));
    }

    #endregion

    #region Wrong-driver-kind fail-fast (BLD-MAJ-6)

    private static async Task<FluentDockerKernel> CreateNoPortsKernelAsync(string driverId)
    {
      var kernel = new FluentDockerKernel(new DriverRegistry(NullLoggerFactory.Instance), NullLoggerFactory.Instance);
      await kernel.RegisterDriverPackAsync(driverId, new NoPortsPack(), new DriverContext(driverId));
      return kernel;
    }

    // A "podman" kernel whose mock pack resolves the pod port, so the WithinPodmanCli capability
    // probe passes (the shared MockDriverPack does not register IPodmanPodDriver by default).
    private static async Task<FluentDockerKernel> CreatePodmanCapableKernelAsync()
    {
      var (kernel, mock) = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("podman");
      mock.RegisterCustomDriver<IPodmanPodDriver>(new Mock<IPodmanPodDriver>().Object);
      return kernel;
    }

    [Fact]
    public async Task WithinDockerCli_DriverWithoutCompose_FailsFast()
    {
      var kernel = await CreateNoPortsKernelAsync("bad");
      try
      {
        Assert.Throws<InterfaceNotSupportedException>(() =>
            new Builder().WithinDockerCli("bad", kernel));
      }
      finally { await kernel.DisposeAsync(); }
    }

    [Fact]
    public async Task WithinPodmanCli_DriverWithoutPods_FailsFast()
    {
      var kernel = await CreateNoPortsKernelAsync("bad");
      try
      {
        Assert.Throws<InterfaceNotSupportedException>(() =>
            new Builder().WithinPodmanCli("bad", kernel));
      }
      finally { await kernel.DisposeAsync(); }
    }

    // A driver pack that resolves no capability ports, so the typed scopes' probes must reject it.
    private sealed class NoPortsPack : IDriverPack
    {
      public DriverType Type => DriverType.DockerCli;
      public RuntimeType Runtime => RuntimeType.Docker;
      public Task InitializeAsync(DriverContext context, CancellationToken cancellationToken = default) => Task.CompletedTask;
      public Task<DriverCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default) => Task.FromResult(DriverCapabilities.Default());
      public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
      public T SysCtl<T>(string driverId) where T : class => throw new InterfaceNotSupportedException(driverId, typeof(T).Name);
      public object SysCtl(string driverId, Type interfaceType) => throw new InterfaceNotSupportedException(driverId, interfaceType.Name);
      public bool TrySysCtl<T>(string driverId, out T instance) where T : class { instance = null!; return false; }
      public bool TryResolve(Type interfaceType, out object implementation) { implementation = null!; return false; }
      public IReadOnlyCollection<Type> GetSupportedInterfaces() => [];
    }

    #endregion
  }
}
