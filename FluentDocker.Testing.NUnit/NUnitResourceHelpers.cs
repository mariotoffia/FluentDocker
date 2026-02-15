using System;
using System.Threading.Tasks;
using FluentDocker.Builders;
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
    /// Disposes a resource and its kernel.
    /// Call from <c>[OneTimeTearDown]</c> or <c>[TearDown]</c>.
    /// </summary>
    public static async Task DisposeAsync(IDockerResource resource, FluentDockerKernel kernel)
    {
      if (resource != null)
        await resource.DisposeAsync();

      kernel?.Dispose();
    }
  }
}
