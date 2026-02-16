using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Podman;
using FluentDocker.Kernel;
using FluentDocker.Testing.Core;

namespace FluentDocker.Testing.NUnit
{
  /// <summary>
  /// NUnit lifecycle helpers for <see cref="ITestResource"/>.
  /// Use in <c>[OneTimeSetUp]</c> and <c>[OneTimeTearDown]</c> for test-class scope,
  /// or in a <c>[SetUpFixture]</c> for assembly scope.
  /// </summary>
  public static class NUnitResourceHelpers
  {
    /// <summary>
    /// Creates and initializes a container resource.
    /// Call from <c>[OneTimeSetUp]</c> or <c>[SetUp]</c>.
    /// </summary>
    public static Task<(FluentDockerKernel kernel, ContainerResource resource)>
        CreateContainerAsync(
            Action<IContainerBuilder> configure,
            Func<Task<FluentDockerKernel>> kernelFactory = null,
            DockerResourceOptions options = null,
            CancellationToken cancellationToken = default)
        => ResourceLifecycle.CreateAndInitializeAsync(
            kernel => new ContainerResource(kernel, configure, options),
            kernelFactory,
            cancellationToken: cancellationToken);

    /// <summary>
    /// Creates and initializes a compose resource.
    /// </summary>
    public static Task<(FluentDockerKernel kernel, ComposeResource resource)>
        CreateComposeAsync(
            Action<IComposeBuilder> configure,
            Func<Task<FluentDockerKernel>> kernelFactory = null,
            DockerResourceOptions options = null,
            CancellationToken cancellationToken = default)
        => ResourceLifecycle.CreateAndInitializeAsync(
            kernel => new ComposeResource(kernel, configure, options),
            kernelFactory,
            cancellationToken: cancellationToken);

    /// <summary>
    /// Creates and initializes a topology resource.
    /// </summary>
    public static Task<(FluentDockerKernel kernel, TopologyResource resource)>
        CreateTopologyAsync(
            Action<Builder> configure,
            Func<Task<FluentDockerKernel>> kernelFactory = null,
            DockerResourceOptions options = null,
            CancellationToken cancellationToken = default)
        => ResourceLifecycle.CreateAndInitializeAsync(
            kernel => new TopologyResource(kernel, configure, options),
            kernelFactory,
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
            Func<Task<FluentDockerKernel>> kernelFactory = null,
            DockerResourceOptions options = null,
            CancellationToken cancellationToken = default)
        => ResourceLifecycle.CreateAndInitializeAsync(
            kernel => new SwarmStackResource(kernel, config, options),
            kernelFactory,
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
            Func<Task<FluentDockerKernel>> kernelFactory = null,
            DockerResourceOptions options = null,
            CancellationToken cancellationToken = default)
        => ResourceLifecycle.CreateAndInitializeAsync(
            kernel => new PodmanKubernetesResource(kernel, config, options),
            kernelFactory,
            ResourceLifecycle.CreateDefaultPodmanKernelAsync,
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
            Func<Task<FluentDockerKernel>> kernelFactory = null,
            CancellationToken cancellationToken = default)
        where TResource : class, ITestResource
        => ResourceLifecycle.CreateAndInitializeAsync(
            resourceFactory, kernelFactory,
            cancellationToken: cancellationToken);

    /// <summary>
    /// Disposes a resource and its kernel.
    /// Call from <c>[OneTimeTearDown]</c> or <c>[TearDown]</c>.
    /// </summary>
    public static Task DisposeAsync(ITestResource resource, FluentDockerKernel kernel)
        => ResourceLifecycle.DisposeAsync(resource, kernel);
  }
}
