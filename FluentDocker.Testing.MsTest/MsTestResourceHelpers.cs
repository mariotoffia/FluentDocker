using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Podman;
using FluentDocker.Kernel;
using FluentDocker.Testing.Core;

namespace FluentDocker.Testing.MsTest
{
  /// <summary>
  /// MSTest lifecycle helpers for <see cref="ITestResource"/>.
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
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>A tuple of the kernel and initialized resource.
    /// Store both so you can dispose them in cleanup.</returns>
    public static Task<(FluentDockerKernel kernel, ContainerResource resource)>
        CreateContainerAsync(
            Action<IContainerBuilder> configure,
            Func<Task<FluentDockerKernel>>? kernelFactory = null,
            DockerResourceOptions? options = null,
            CancellationToken cancellationToken = default)
        => ResourceLifecycle.CreateAndInitializeAsync(
            kernel => new ContainerResource(kernel, configure, options!),
            kernelFactory!,
            cancellationToken: cancellationToken);

    /// <summary>
    /// Creates and initializes a compose resource.
    /// </summary>
    public static Task<(FluentDockerKernel kernel, ComposeResource resource)>
        CreateComposeAsync(
            Action<IComposeBuilder> configure,
            Func<Task<FluentDockerKernel>>? kernelFactory = null,
            DockerResourceOptions? options = null,
            CancellationToken cancellationToken = default)
        => ResourceLifecycle.CreateAndInitializeAsync(
            kernel => new ComposeResource(kernel, configure, options!),
            kernelFactory!,
            cancellationToken: cancellationToken);

    /// <summary>
    /// Creates and initializes a topology resource.
    /// </summary>
    public static Task<(FluentDockerKernel kernel, TopologyResource resource)>
        CreateTopologyAsync(
            Action<Builder> configure,
            Func<Task<FluentDockerKernel>>? kernelFactory = null,
            DockerResourceOptions? options = null,
            CancellationToken cancellationToken = default)
        => ResourceLifecycle.CreateAndInitializeAsync(
            kernel => new TopologyResource(kernel, configure, options!),
            kernelFactory!,
            cancellationToken: cancellationToken);

    /// <summary>
    /// Creates and initializes a swarm stack resource.
    /// </summary>
    /// <param name="config">Stack deploy configuration.</param>
    /// <param name="kernelFactory">Optional kernel factory. Defaults to Docker CLI.</param>
    /// <param name="options">Optional resource options.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    public static Task<(FluentDockerKernel kernel, SwarmStackResource resource)>
        CreateSwarmStackAsync(
            StackDeployConfig config,
            Func<Task<FluentDockerKernel>>? kernelFactory = null,
            DockerResourceOptions? options = null,
            CancellationToken cancellationToken = default)
        => ResourceLifecycle.CreateAndInitializeAsync(
            kernel => new SwarmStackResource(kernel, config, options!),
            kernelFactory!,
            cancellationToken: cancellationToken);

    /// <summary>
    /// Creates and initializes a Podman Kubernetes resource.
    /// </summary>
    /// <param name="config">Kubernetes play configuration.</param>
    /// <param name="kernelFactory">Optional kernel factory. Defaults to Podman CLI.</param>
    /// <param name="options">Optional resource options.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    public static Task<(FluentDockerKernel kernel, PodmanKubernetesResource resource)>
        CreatePodmanKubernetesAsync(
            KubePlayConfig config,
            Func<Task<FluentDockerKernel>>? kernelFactory = null,
            DockerResourceOptions? options = null,
            CancellationToken cancellationToken = default)
        => ResourceLifecycle.CreateAndInitializeAsync(
            kernel => new PodmanKubernetesResource(kernel, config, options!),
            kernelFactory!,
            () => ResourceLifecycle.CreateDefaultPodmanKernelAsync(),
            cancellationToken);

    /// <summary>
    /// Creates and initializes any <see cref="ITestResource"/> using a factory.
    /// Use this for plugin resources or custom resource types.
    /// </summary>
    /// <typeparam name="TResource">The resource type to create.</typeparam>
    /// <param name="resourceFactory">Factory receiving a kernel; returns the resource.</param>
    /// <param name="kernelFactory">Optional kernel factory. Defaults to Docker CLI.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    public static Task<(FluentDockerKernel kernel, TResource resource)>
        CreateResourceAsync<TResource>(
            Func<FluentDockerKernel, TResource> resourceFactory,
            Func<Task<FluentDockerKernel>>? kernelFactory = null,
            CancellationToken cancellationToken = default)
        where TResource : class, ITestResource
        => ResourceLifecycle.CreateAndInitializeAsync(
            resourceFactory, kernelFactory!,
            cancellationToken: cancellationToken);

    /// <summary>
    /// Disposes a resource and its kernel.
    /// Call from <c>[TestCleanup]</c> or <c>[ClassCleanup]</c>.
    /// </summary>
    public static Task DisposeAsync(ITestResource resource, FluentDockerKernel kernel)
        => ResourceLifecycle.DisposeAsync(resource, kernel);
  }
}
