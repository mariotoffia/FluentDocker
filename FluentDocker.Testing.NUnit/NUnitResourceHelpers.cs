using System;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Podman;
using FluentDocker.Kernel;
using FluentDocker.Testing.Core;

namespace FluentDocker.Testing.NUnit
{
  /// <summary>
  /// NUnit lifecycle helpers for <see cref="IDockerResource"/>.
  /// Use in <c>[OneTimeSetUp]</c> and <c>[OneTimeTearDown]</c> for test-class scope,
  /// or in a <c>[SetUpFixture]</c> for assembly scope.
  /// </summary>
  public static class NUnitResourceHelpers
  {
    /// <summary>
    /// Creates and initializes a container resource.
    /// Call from <c>[OneTimeSetUp]</c> or <c>[SetUp]</c>.
    /// </summary>
    public static async Task<(FluentDockerKernel kernel, ContainerResource resource)>
        CreateContainerAsync(
            Action<IContainerBuilder> configure,
            Func<Task<FluentDockerKernel>> kernelFactory = null,
            DockerResourceOptions options = null)
    {
      FluentDockerKernel kernel = null;
      ContainerResource resource = null;
      try
      {
        kernel = kernelFactory != null
            ? await kernelFactory()
            : await FluentDockerKernel.Create()
                .WithDockerCli("docker-cli", d => d.AsDefault())
                .BuildAsync();

        resource = new ContainerResource(kernel, configure, options);
        await resource.InitializeAsync();
        return (kernel, resource);
      }
      catch
      {
        try { if (resource != null) await resource.DisposeAsync(); } catch { /* best effort */ }
        kernel?.Dispose();
        throw;
      }
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
      FluentDockerKernel kernel = null;
      ComposeResource resource = null;
      try
      {
        kernel = kernelFactory != null
            ? await kernelFactory()
            : await FluentDockerKernel.Create()
                .WithDockerCli("docker-cli", d => d.AsDefault())
                .BuildAsync();

        resource = new ComposeResource(kernel, configure, options);
        await resource.InitializeAsync();
        return (kernel, resource);
      }
      catch
      {
        try { if (resource != null) await resource.DisposeAsync(); } catch { /* best effort */ }
        kernel?.Dispose();
        throw;
      }
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
      FluentDockerKernel kernel = null;
      TopologyResource resource = null;
      try
      {
        kernel = kernelFactory != null
            ? await kernelFactory()
            : await FluentDockerKernel.Create()
                .WithDockerCli("docker-cli", d => d.AsDefault())
                .BuildAsync();

        resource = new TopologyResource(kernel, configure, options);
        await resource.InitializeAsync();
        return (kernel, resource);
      }
      catch
      {
        try { if (resource != null) await resource.DisposeAsync(); } catch { /* best effort */ }
        kernel?.Dispose();
        throw;
      }
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
      FluentDockerKernel kernel = null;
      SwarmStackResource resource = null;
      try
      {
        kernel = kernelFactory != null
            ? await kernelFactory()
            : await FluentDockerKernel.Create()
                .WithDockerCli("docker-cli", d => d.AsDefault())
                .BuildAsync();

        resource = new SwarmStackResource(kernel, config, options);
        await resource.InitializeAsync();
        return (kernel, resource);
      }
      catch
      {
        try { if (resource != null) await resource.DisposeAsync(); } catch { /* best effort */ }
        kernel?.Dispose();
        throw;
      }
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
      FluentDockerKernel kernel = null;
      PodmanKubernetesResource resource = null;
      try
      {
        kernel = kernelFactory != null
            ? await kernelFactory()
            : await FluentDockerKernel.Create()
                .WithPodmanCli("podman-cli", d => d.AsDefault())
                .BuildAsync();

        resource = new PodmanKubernetesResource(kernel, config, options);
        await resource.InitializeAsync();
        return (kernel, resource);
      }
      catch
      {
        try { if (resource != null) await resource.DisposeAsync(); } catch { /* best effort */ }
        kernel?.Dispose();
        throw;
      }
    }

    /// <summary>
    /// Creates and initializes any <see cref="IDockerResource"/> using a factory.
    /// Use this for plugin resources or custom resource types.
    /// </summary>
    /// <typeparam name="TResource">The resource type to create.</typeparam>
    /// <param name="resourceFactory">Factory receiving a kernel; returns the resource.</param>
    /// <param name="kernelFactory">Optional kernel factory. Defaults to Docker CLI.</param>
    public static async Task<(FluentDockerKernel kernel, TResource resource)>
        CreateResourceAsync<TResource>(
            Func<FluentDockerKernel, TResource> resourceFactory,
            Func<Task<FluentDockerKernel>> kernelFactory = null)
        where TResource : class, IDockerResource
    {
      ArgumentNullException.ThrowIfNull(resourceFactory);

      FluentDockerKernel kernel = null;
      TResource resource = null;
      try
      {
        kernel = kernelFactory != null
            ? await kernelFactory()
            : await FluentDockerKernel.Create()
                .WithDockerCli("docker-cli", d => d.AsDefault())
                .BuildAsync();

        resource = resourceFactory(kernel)
            ?? throw new InvalidOperationException(
                "resourceFactory returned null. The factory must return a non-null resource.");
        await resource.InitializeAsync();
        return (kernel, resource);
      }
      catch
      {
        try { if (resource != null) await resource.DisposeAsync(); } catch { /* best effort */ }
        kernel?.Dispose();
        throw;
      }
    }

    /// <summary>
    /// Disposes a resource and its kernel.
    /// Call from <c>[OneTimeTearDown]</c> or <c>[TearDown]</c>.
    /// </summary>
    public static async Task DisposeAsync(IDockerResource resource, FluentDockerKernel kernel)
    {
      try
      {
        if (resource != null)
          await resource.DisposeAsync();
      }
      finally
      {
        kernel?.Dispose();
      }
    }
  }
}
