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
  /// Abstract base class for xUnit tests backed by a single container.
  /// Implements <see cref="IAsyncLifetime"/> so initialization is fully async —
  /// no sync-over-async <c>GetAwaiter().GetResult()</c> needed in constructors.
  /// </summary>
  /// <remarks>
  /// <para>Override <see cref="ConfigureContainer"/> to specify the container
  /// image and settings. Optionally override <see cref="GetOptions"/> for
  /// resource options or <see cref="KernelFactory"/> for a custom kernel.</para>
  /// <para>Usage:</para>
  /// <code>
  /// public class NginxTests : XunitContainerTestBase
  /// {
  ///   protected override void ConfigureContainer(IContainerBuilder b)
  ///       => b.UseImage("nginx:latest");
  ///
  ///   [Fact]
  ///   public async Task Container_IsRunning()
  ///   {
  ///     var info = await Resource.InspectAsync();
  ///     Assert.Equal("running", info.State.Status);
  ///   }
  /// }
  /// </code>
  /// </remarks>
  public abstract class XunitContainerTestBase : IAsyncLifetime
  {
    /// <summary>
    /// The underlying container resource, available after initialization.
    /// </summary>
    public ContainerResource? Resource { get; private set; }

    /// <summary>
    /// Shorthand access to the running container service.
    /// </summary>
    public IContainerService? Container => Resource?.Container;

    /// <summary>
    /// The kernel managing drivers for this test.
    /// </summary>
    public FluentDockerKernel? Kernel { get; private set; }

    /// <summary>
    /// Override to configure the container. Called during initialization.
    /// </summary>
    protected abstract void ConfigureContainer(IContainerBuilder builder);

    /// <summary>
    /// Override to provide custom resource options. Returns null for defaults.
    /// </summary>
    protected virtual DockerResourceOptions? GetOptions() => null;

    /// <summary>
    /// Override to provide a custom kernel factory.
    /// Returns null to use the default Docker CLI kernel.
    /// </summary>
    protected virtual Func<Task<FluentDockerKernel>>? KernelFactory => null;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
      if (Resource != null)
        throw new InvalidOperationException(
            "Already initialized. Dispose before re-initializing.");

      var (kernel, resource) = await ResourceLifecycle.CreateAndInitializeAsync(
          k => new ContainerResource(k, ConfigureContainer, GetOptions()!),
          KernelFactory!);

      Kernel = kernel;
      Resource = resource;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
      try
      {
        await ResourceLifecycle.DisposeAsync(Resource!, Kernel!);
      }
      finally
      {
        Resource = null;
        Kernel = null;
      }

      GC.SuppressFinalize(this);
    }
  }
}
