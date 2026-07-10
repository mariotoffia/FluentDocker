using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Common;
using FluentDocker.Kernel;
using FluentDocker.Services;
using FluentDocker.Testing.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FluentDocker.Testing.MsTest
{
  /// <summary>
  /// Abstract base class for MSTest test classes backed by a container that is created
  /// and destroyed <b>per test method</b> (via <c>[TestInitialize]</c>/<c>[TestCleanup]</c>).
  /// Every test method gets a fresh container.
  /// </summary>
  /// <remarks>
  /// <para>The per-test lifetime is deliberate and explicit in the name: a class with N test
  /// methods provisions N containers. For a single container shared by all methods in the class,
  /// use <see cref="MsTestClassContainerFixtureBase{TFixture}"/> instead — porting a suite onto the
  /// wrong base multiplies container churn 10-100×.</para>
  /// <para>Subclass and override <see cref="ConfigureContainer"/> to specify the container image and
  /// settings. Annotate your test class with <c>[TestClass]</c>.</para>
  /// <para>Usage:</para>
  /// <code>
  /// [TestClass]
  /// public class NginxTests : MsTestPerTestContainerFixtureBase
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
  public abstract class MsTestPerTestContainerFixtureBase
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

    /// <summary>
    /// When true, an unavailable Docker-compatible runtime marks the test inconclusive.
    /// </summary>
    protected virtual bool SkipWhenUnavailable => false;

    /// <summary>Provisions a fresh container before each test method (MSTest <c>[TestInitialize]</c>).</summary>
    [TestInitialize]
    public async Task TestInitializeAsync()
    {
      if (_resource != null)
        throw new InvalidOperationException(
            "Already initialized. Dispose before re-initializing.");

      (FluentDockerKernel kernel, ContainerResource resource) result;
      try
      {
        result = await ResourceLifecycle.CreateAndInitializeAsync(
            k => new ContainerResource(k, ConfigureContainer, GetOptions()!),
            KernelFactory!).ConfigureAwait(false);
      }
      catch (ResourceInitializationException ex)
          when (SkipWhenUnavailable && ex.InnerException is FluentDockerUnavailableException)
      {
        Assert.Inconclusive(ex.InnerException.Message);
        return;
      }
      catch (DriverNotAvailableException ex) when (SkipWhenUnavailable)
      {
        Assert.Inconclusive(ex.Message);
        return;
      }

      _kernel = result.kernel;
      _resource = result.resource;
    }

    /// <summary>Tears down the per-test container after each test method (MSTest <c>[TestCleanup]</c>).</summary>
    [TestCleanup]
    public async Task TestCleanupAsync()
    {
      // Clear handles only AFTER successful disposal. If cleanup throws, the public
      // Resource/Kernel handles stay non-null for LastTeardownDiagnostics and label-based
      // manual cleanup (docker/podman rm -f by the session label). The kernel is already
      // disposed by ResourceLifecycle.DisposeAsync, so it cannot be reused to retry teardown;
      // recover via the next run's orphan sweep or a manual label sweep. The exception propagates.
      await ResourceLifecycle.DisposeAsync(_resource!, _kernel!).ConfigureAwait(false);
      _resource = null;
      _kernel = null;
    }

    private void EnsureInitialized()
    {
      if (_resource == null)
        throw new InvalidOperationException(
            "Fixture has not been initialized. Call TestInitializeAsync first.");
    }
  }
}
