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
  /// Docker Compose service. Implements <see cref="IAsyncLifetime"/> so xUnit
  /// handles initialization and disposal automatically — no sync-over-async
  /// <c>GetAwaiter().GetResult()</c> needed.
  /// </summary>
  /// <remarks>
  /// <para>Subclass this and override <see cref="ConfigureCompose"/> to
  /// specify the compose file and settings. Use the subclass with
  /// <c>IClassFixture&lt;T&gt;</c> or <c>ICollectionFixture&lt;T&gt;</c>.</para>
  /// <para>Usage:</para>
  /// <code>
  /// public class AppFixture : XunitComposeFixtureBase
  /// {
  ///   protected override void ConfigureCompose(IComposeBuilder b)
  ///       =&gt; b.FromFile("docker-compose.yml");
  /// }
  ///
  /// public class AppTests : IClassFixture&lt;AppFixture&gt;
  /// {
  ///   private readonly AppFixture _fixture;
  ///   public AppTests(AppFixture fixture) =&gt; _fixture = fixture;
  ///
  ///   [Fact]
  ///   public void Service_IsAvailable()
  ///       =&gt; Assert.NotNull(_fixture.Service);
  /// }
  /// </code>
  /// </remarks>
  public abstract class XunitComposeFixtureBase : IAsyncLifetime
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
    /// The kernel managing drivers for this fixture.
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
