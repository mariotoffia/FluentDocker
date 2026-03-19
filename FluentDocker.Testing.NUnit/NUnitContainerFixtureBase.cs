using System;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Kernel;
using FluentDocker.Services;
using FluentDocker.Testing.Core;
using NUnit.Framework;

namespace FluentDocker.Testing.NUnit
{
  /// <summary>
  /// Abstract base class for NUnit fixtures backed by a single container.
  /// Uses <c>[OneTimeSetUp]</c> and <c>[OneTimeTearDown]</c> so NUnit handles
  /// initialization and disposal automatically.
  /// </summary>
  /// <remarks>
  /// <para>Subclass and override <see cref="ConfigureContainer"/> to specify the
  /// container image and settings. Annotate your test class with <c>[TestFixture]</c>.</para>
  /// <para>Usage:</para>
  /// <code>
  /// [TestFixture]
  /// public class NginxTests : NUnitContainerFixtureBase
  /// {
  ///   protected override void ConfigureContainer(IContainerBuilder b)
  ///       =&gt; b.UseImage("nginx:latest");
  ///
  ///   [Test]
  ///   public void Container_IsRunning()
  ///       =&gt; Assert.That(Container, Is.Not.Null);
  /// }
  /// </code>
  /// </remarks>
  public abstract class NUnitContainerFixtureBase
  {
    /// <summary>
    /// The underlying container resource, available after setup.
    /// </summary>
    public ContainerResource? Resource { get; private set; }

    /// <summary>
    /// Shorthand access to the running container service.
    /// </summary>
    public IContainerService? Container => Resource?.Container;

    /// <summary>
    /// The kernel managing drivers for this fixture.
    /// </summary>
    public FluentDockerKernel? Kernel { get; private set; }

    /// <summary>
    /// Override to configure the container. Called during setup.
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

    [OneTimeSetUp]
    public async Task SetUpAsync()
    {
      var (kernel, resource) = await ResourceLifecycle.CreateAndInitializeAsync(
          k => new ContainerResource(k, ConfigureContainer, GetOptions()!),
          KernelFactory!).ConfigureAwait(false);

      Kernel = kernel;
      Resource = resource;
    }

    [OneTimeTearDown]
    public async Task TearDownAsync()
    {
      try
      {
        await ResourceLifecycle.DisposeAsync(Resource!, Kernel!).ConfigureAwait(false);
      }
      finally
      {
        Resource = null;
        Kernel = null;
      }
    }
  }
}
