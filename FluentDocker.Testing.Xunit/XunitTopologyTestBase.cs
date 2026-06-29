using System;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Kernel;
using FluentDocker.Testing.Core;
using Xunit;

namespace FluentDocker.Testing.Xunit
{
  /// <summary>
  /// Abstract base class for xUnit tests backed by a multi-container topology.
  /// Implements <see cref="IAsyncLifetime"/> so initialization is fully async.
  /// </summary>
  /// <remarks>
  /// <para>Override <see cref="ConfigureTopology"/> to define the topology
  /// (containers, networks, volumes). Optionally override <see cref="GetOptions"/>
  /// for resource options or <see cref="KernelFactory"/> for a custom kernel.</para>
  /// </remarks>
  public abstract class XunitTopologyTestBase : IAsyncLifetime
  {
    private TopologyResource? _resource;
    private FluentDockerKernel? _kernel;

    /// <summary>
    /// The underlying topology resource, available after initialization.
    /// </summary>
    public TopologyResource Resource
    {
      get { EnsureInitialized(); return _resource!; }
    }

    /// <summary>
    /// The kernel managing drivers for this test.
    /// </summary>
    public FluentDockerKernel Kernel
    {
      get { EnsureInitialized(); return _kernel!; }
    }

    /// <summary>
    /// Override to configure the topology. Called during initialization.
    /// </summary>
    protected abstract void ConfigureTopology(Builder builder);

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
      if (_resource != null)
        throw new InvalidOperationException(
            "Already initialized. Dispose before re-initializing.");

      var (kernel, resource) = await ResourceLifecycle.CreateAndInitializeAsync(
          k => new TopologyResource(k, ConfigureTopology, GetOptions()!),
          KernelFactory!).ConfigureAwait(false);

      _kernel = kernel;
      _resource = resource;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
      try
      {
        await ResourceLifecycle.DisposeAsync(_resource!, _kernel!).ConfigureAwait(false);
      }
      finally
      {
        _resource = null;
        _kernel = null;
      }

      GC.SuppressFinalize(this);
    }

    private void EnsureInitialized()
    {
      if (_resource == null)
        throw new InvalidOperationException(
            "Fixture has not been initialized. Call InitializeAsync first.");
    }
  }
}
