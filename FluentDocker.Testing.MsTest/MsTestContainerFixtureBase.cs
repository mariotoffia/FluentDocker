using System;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Kernel;
using FluentDocker.Services;
using FluentDocker.Testing.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FluentDocker.Testing.MsTest
{
  /// <summary>
  /// Abstract base class for MSTest test classes backed by a single container.
  /// Uses <c>[ClassInitialize]</c> and <c>[ClassCleanup]</c> semantics via
  /// <c>[TestInitialize]</c>/<c>[TestCleanup]</c> with lazy initialization
  /// so the container is created once per test class.
  /// </summary>
  /// <remarks>
  /// <para>Subclass and override <see cref="ConfigureContainer"/> to specify the
  /// container image and settings. Annotate your test class with <c>[TestClass]</c>.</para>
  /// <para>Usage:</para>
  /// <code>
  /// [TestClass]
  /// public class NginxTests : MsTestContainerFixtureBase
  /// {
  ///   protected override void ConfigureContainer(IContainerBuilder b)
  ///       =&gt; b.UseImage("nginx:latest");
  ///
  ///   [TestMethod]
  ///   public void Container_IsRunning()
  ///       =&gt; Assert.IsNotNull(Container);
  /// }
  /// </code>
  /// </remarks>
#pragma warning disable CA1822 // Instance properties intentional — provides consistent API across test frameworks
  public abstract class MsTestContainerFixtureBase
  {
    private static readonly object Lock = new();
    private static ContainerResource? _resource;
    private static FluentDockerKernel? _kernel;
    private static int _refCount;

    /// <summary>
    /// The underlying container resource, available after initialization.
    /// </summary>
    public ContainerResource? Resource => _resource;

    /// <summary>
    /// Shorthand access to the running container service.
    /// </summary>
    public IContainerService? Container => _resource?.Container;

    /// <summary>
    /// The kernel managing drivers for this fixture.
    /// </summary>
    public FluentDockerKernel? Kernel => _kernel;

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

    [TestInitialize]
    public async Task TestInitializeAsync()
    {
      bool shouldInit;
      lock (Lock)
      {
        shouldInit = _refCount == 0;
        _refCount++;
      }

      if (shouldInit)
      {
        var (kernel, resource) = await ResourceLifecycle.CreateAndInitializeAsync(
            k => new ContainerResource(k, ConfigureContainer, GetOptions()!),
            KernelFactory!).ConfigureAwait(false);

        _kernel = kernel;
        _resource = resource;
      }
    }

    [TestCleanup]
    public async Task TestCleanupAsync()
    {
      bool shouldDispose;
      lock (Lock)
      {
        _refCount--;
        shouldDispose = _refCount == 0;
      }

      if (shouldDispose)
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
      }
    }
  }
#pragma warning restore CA1822
}
