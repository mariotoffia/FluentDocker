using System;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Kernel;
using FluentDocker.Services;
using FluentDocker.Testing.Core;
using Xunit;

namespace FluentDocker.Testing.Xunit
{
  /// <summary>
  /// Abstract base class for xUnit tests backed by a Docker Compose service.
  /// Implements <see cref="IAsyncLifetime"/> so initialization is fully async.
  /// </summary>
  /// <remarks>
  /// <para>Override <see cref="ConfigureCompose"/> to specify the compose file
  /// and settings. Optionally override <see cref="GetOptions"/> for resource
  /// options or <see cref="KernelFactory"/> for a custom kernel.</para>
  /// </remarks>
  public abstract class XunitComposeTestBase : IAsyncLifetime
  {
    /// <summary>
    /// The underlying compose resource, available after initialization.
    /// </summary>
    public ComposeResource Resource { get; private set; }

    /// <summary>
    /// Shorthand access to the running compose service.
    /// </summary>
    public IComposeService Service => Resource?.Service;

    /// <summary>
    /// The kernel managing drivers for this test.
    /// </summary>
    public FluentDockerKernel Kernel { get; private set; }

    /// <summary>
    /// Override to configure the compose service. Called during initialization.
    /// </summary>
    protected abstract void ConfigureCompose(IComposeBuilder builder);

    /// <summary>
    /// Override to provide custom resource options. Returns null for defaults.
    /// </summary>
    protected virtual DockerResourceOptions GetOptions() => null;

    /// <summary>
    /// Override to provide a custom kernel factory.
    /// Returns null to use the default Docker CLI kernel.
    /// </summary>
    protected virtual Func<Task<FluentDockerKernel>> KernelFactory => null;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
      var (kernel, resource) = await ResourceLifecycle.CreateAndInitializeAsync(
          k => new ComposeResource(k, ConfigureCompose, GetOptions()),
          KernelFactory);

      Kernel = kernel;
      Resource = resource;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
      try
      {
        await ResourceLifecycle.DisposeAsync(Resource, Kernel);
      }
      finally
      {
        Resource = null;
        Kernel = null;
      }
    }
  }
}
