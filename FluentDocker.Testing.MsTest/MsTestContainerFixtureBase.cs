using System;
using System.Threading;
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
  /// Uses <c>[TestInitialize]</c>/<c>[TestCleanup]</c> so each test method gets
  /// its own container.
  /// </summary>
  /// <remarks>
  /// <para>Subclass and override <see cref="ConfigureContainer"/> to specify the
  /// container image and settings. Annotate your test class with <c>[TestClass]</c>.</para>
  /// <para>For true class-scoped sharing, use MSTest <c>[ClassInitialize]</c> and
  /// <c>[ClassCleanup]</c> with <see cref="MsTestResourceHelpers"/>.</para>
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
  public abstract class MsTestContainerFixtureBase
  {
    private ContainerResource? _resource;
    private FluentDockerKernel? _kernel;

    /// <summary>
    /// The underlying container resource, available after initialization.
    /// </summary>
    public ContainerResource Resource
    {
      get { EnsureInitialized(); return _resource!; }
    }

    /// <summary>
    /// Shorthand access to the running container service.
    /// </summary>
    public IContainerService Container => Resource.Container;

    /// <summary>
    /// The kernel managing drivers for this fixture.
    /// </summary>
    public FluentDockerKernel Kernel
    {
      get { EnsureInitialized(); return _kernel!; }
    }

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
      if (_resource != null)
        throw new InvalidOperationException(
            "Already initialized. Dispose before re-initializing.");

      var (kernel, resource) = await ResourceLifecycle.CreateAndInitializeAsync(
          k => new ContainerResource(k, ConfigureContainer, GetOptions()!),
          KernelFactory!).ConfigureAwait(false);

      _kernel = kernel;
      _resource = resource;
    }

    [TestCleanup]
    public async Task TestCleanupAsync()
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

    private void EnsureInitialized()
    {
      if (_resource == null)
        throw new InvalidOperationException(
            "Fixture has not been initialized. Call TestInitializeAsync first.");
    }
  }
}
