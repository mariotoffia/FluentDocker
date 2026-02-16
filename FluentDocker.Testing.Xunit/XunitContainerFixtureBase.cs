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
  /// Abstract base class for xUnit class/collection fixtures backed by a
  /// single container. Implements <see cref="IAsyncLifetime"/> so xUnit
  /// handles initialization and disposal automatically — no sync-over-async
  /// <c>GetAwaiter().GetResult()</c> needed.
  /// </summary>
  /// <remarks>
  /// <para>Subclass this and override <see cref="ConfigureContainer"/> to
  /// specify the container image and settings. Use the subclass with
  /// <c>IClassFixture&lt;T&gt;</c> or <c>ICollectionFixture&lt;T&gt;</c>.</para>
  /// <para>Usage:</para>
  /// <code>
  /// public class NginxFixture : XunitContainerFixtureBase
  /// {
  ///   protected override void ConfigureContainer(IContainerBuilder b)
  ///       =&gt; b.UseImage("nginx:latest");
  /// }
  ///
  /// public class NginxTests : IClassFixture&lt;NginxFixture&gt;
  /// {
  ///   private readonly NginxFixture _fixture;
  ///   public NginxTests(NginxFixture fixture) =&gt; _fixture = fixture;
  ///
  ///   [Fact]
  ///   public void Container_IsRunning()
  ///       =&gt; Assert.NotNull(_fixture.Container);
  /// }
  /// </code>
  /// </remarks>
  public abstract class XunitContainerFixtureBase : IAsyncLifetime
  {
    /// <summary>
    /// The underlying container resource, available after initialization.
    /// </summary>
    public ContainerResource Resource { get; private set; }

    /// <summary>
    /// Shorthand access to the running container service.
    /// </summary>
    public IContainerService Container => Resource?.Container;

    /// <summary>
    /// The kernel managing drivers for this fixture.
    /// </summary>
    public FluentDockerKernel Kernel { get; private set; }

    /// <summary>
    /// Override to configure the container. Called during initialization.
    /// </summary>
    protected abstract void ConfigureContainer(IContainerBuilder builder);

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
          k => new ContainerResource(k, ConfigureContainer, GetOptions()),
          KernelFactory);

      Kernel = kernel;
      Resource = resource;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
      try
      {
        if (Resource != null)
          await Resource.DisposeAsync();
      }
      finally
      {
        if (Kernel != null)
          await Kernel.DisposeAsync();
        Resource = null;
        Kernel = null;
      }
    }
  }
}
