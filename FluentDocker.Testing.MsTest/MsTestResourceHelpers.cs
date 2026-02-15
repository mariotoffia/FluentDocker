using System;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Podman;
using FluentDocker.Kernel;
using FluentDocker.Services;
using FluentDocker.Testing.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FluentDocker.Testing.MsTest
{
  /// <summary>
  /// MSTest lifecycle helpers for <see cref="IDockerResource"/>.
  /// Use in <c>[TestInitialize]</c> and <c>[TestCleanup]</c> or
  /// <c>[ClassInitialize]</c> and <c>[ClassCleanup]</c>.
  /// </summary>
  public static class MsTestResourceHelpers
  {
    /// <summary>
    /// Creates and initializes a container resource.
    /// Call from <c>[TestInitialize]</c> or <c>[ClassInitialize]</c>.
    /// </summary>
    /// <param name="configure">Container builder configuration.</param>
    /// <param name="kernelFactory">Optional kernel factory. Defaults to Docker CLI.</param>
    /// <param name="options">Optional resource options.</param>
    /// <returns>A tuple of the kernel and initialized resource.
    /// Store both so you can dispose them in cleanup.</returns>
    public static async Task<(FluentDockerKernel kernel, ContainerResource resource)>
        CreateContainerAsync(
            Action<IContainerBuilder> configure,
            Func<Task<FluentDockerKernel>> kernelFactory = null,
            DockerResourceOptions options = null)
    {
      var kernel = kernelFactory != null
          ? await kernelFactory()
          : await FluentDockerKernel.Create()
              .WithDockerCli("docker-cli", d => d.AsDefault())
              .BuildAsync();

      var resource = new ContainerResource(kernel, configure, options);
      await resource.InitializeAsync();
      return (kernel, resource);
    }

    /// <summary>
    /// Creates and initializes a compose resource.
    /// </summary>
    public static async Task<(FluentDockerKernel kernel, ComposeResource resource)>
        CreateComposeAsync(
            Action<IComposeBuilder> configure,
            Func<Task<FluentDockerKernel>> kernelFactory = null,
            DockerResourceOptions options = null)
    {
      var kernel = kernelFactory != null
          ? await kernelFactory()
          : await FluentDockerKernel.Create()
              .WithDockerCli("docker-cli", d => d.AsDefault())
              .BuildAsync();

      var resource = new ComposeResource(kernel, configure, options);
      await resource.InitializeAsync();
      return (kernel, resource);
    }

    /// <summary>
    /// Creates and initializes a topology resource.
    /// </summary>
    public static async Task<(FluentDockerKernel kernel, TopologyResource resource)>
        CreateTopologyAsync(
            Action<Builder> configure,
            Func<Task<FluentDockerKernel>> kernelFactory = null,
            DockerResourceOptions options = null)
    {
      var kernel = kernelFactory != null
          ? await kernelFactory()
          : await FluentDockerKernel.Create()
              .WithDockerCli("docker-cli", d => d.AsDefault())
              .BuildAsync();

      var resource = new TopologyResource(kernel, configure, options);
      await resource.InitializeAsync();
      return (kernel, resource);
    }

    /// <summary>
    /// Creates and initializes a swarm stack resource.
    /// </summary>
    /// <param name="config">Stack deploy configuration.</param>
    /// <param name="kernelFactory">Optional kernel factory. Defaults to Docker CLI.</param>
    /// <param name="options">Optional resource options.</param>
    public static async Task<(FluentDockerKernel kernel, SwarmStackResource resource)>
        CreateSwarmStackAsync(
            StackDeployConfig config,
            Func<Task<FluentDockerKernel>> kernelFactory = null,
            DockerResourceOptions options = null)
    {
      var kernel = kernelFactory != null
          ? await kernelFactory()
          : await FluentDockerKernel.Create()
              .WithDockerCli("docker-cli", d => d.AsDefault())
              .BuildAsync();

      var resource = new SwarmStackResource(kernel, config, options);
      await resource.InitializeAsync();
      return (kernel, resource);
    }

    /// <summary>
    /// Creates and initializes a Podman Kubernetes resource.
    /// </summary>
    /// <param name="config">Kubernetes play configuration.</param>
    /// <param name="kernelFactory">Optional kernel factory. Defaults to Podman CLI.</param>
    /// <param name="options">Optional resource options.</param>
    public static async Task<(FluentDockerKernel kernel, PodmanKubernetesResource resource)>
        CreatePodmanKubernetesAsync(
            KubePlayConfig config,
            Func<Task<FluentDockerKernel>> kernelFactory = null,
            DockerResourceOptions options = null)
    {
      var kernel = kernelFactory != null
          ? await kernelFactory()
          : await FluentDockerKernel.Create()
              .WithPodmanCli("podman-cli", d => d.AsDefault())
              .BuildAsync();

      var resource = new PodmanKubernetesResource(kernel, config, options);
      await resource.InitializeAsync();
      return (kernel, resource);
    }

    /// <summary>
    /// Disposes a resource and its kernel.
    /// Call from <c>[TestCleanup]</c> or <c>[ClassCleanup]</c>.
    /// </summary>
    public static async Task DisposeAsync(IDockerResource resource, FluentDockerKernel kernel)
    {
      if (resource != null)
        await resource.DisposeAsync();

      kernel?.Dispose();
    }
  }
}
