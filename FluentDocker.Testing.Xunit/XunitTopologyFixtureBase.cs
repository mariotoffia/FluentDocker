using System;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Kernel;
using FluentDocker.Testing.Core;
using Xunit;

namespace FluentDocker.Testing.Xunit
{
  /// <summary>
  /// Abstract base class for xUnit class/collection fixtures backed by a
  /// multi-container topology. Implements <see cref="IAsyncLifetime"/> so xUnit
  /// handles initialization and disposal automatically — no sync-over-async
  /// <c>GetAwaiter().GetResult()</c> needed.
  /// </summary>
  /// <remarks>
  /// <para>Subclass this and override <see cref="ConfigureTopology"/> to
  /// define the topology (containers, networks, volumes). Use the subclass with
  /// <c>IClassFixture&lt;T&gt;</c> or <c>ICollectionFixture&lt;T&gt;</c>.</para>
  /// <para>Usage:</para>
  /// <code>
  /// public class AppStackFixture : XunitTopologyFixtureBase
  /// {
  ///   protected override void ConfigureTopology(Builder b)
  ///       =&gt; b.WithinDriver().UseContainer(c =&gt; c.UseImage("redis:latest"));
  /// }
  ///
  /// public class AppStackTests : IClassFixture&lt;AppStackFixture&gt;
  /// {
  ///   private readonly AppStackFixture _fixture;
  ///   public AppStackTests(AppStackFixture fixture) =&gt; _fixture = fixture;
  ///
  ///   [Fact]
  ///   public void Topology_IsInitialized()
  ///       =&gt; Assert.NotNull(_fixture.Resource);
  /// }
  /// </code>
  /// </remarks>
  public abstract class XunitTopologyFixtureBase : IAsyncLifetime
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
    /// The kernel managing drivers for this fixture.
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
      // Clear handles only AFTER successful disposal. If cleanup throws, the public
      // Resource/Kernel handles stay available for LastTeardownDiagnostics, retry, or
      // manual cleanup, and the exception propagates.
      await ResourceLifecycle.DisposeAsync(_resource!, _kernel!).ConfigureAwait(false);
      _resource = null;
      _kernel = null;
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
