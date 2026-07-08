using System;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Common;
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
    private ContainerResource? _resource;
    private FluentDockerKernel? _kernel;

    /// <summary>
    /// The underlying container resource, available after setup.
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

    /// <summary>
    /// When true, an unavailable Docker-compatible runtime marks the fixture ignored.
    /// </summary>
    protected virtual bool SkipWhenUnavailable => false;

    [OneTimeSetUp]
    public async Task SetUpAsync()
    {
      if (_resource != null)
        return;

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
        Assert.Ignore(ex.InnerException.Message);
        return;
      }
      catch (DriverNotAvailableException ex) when (SkipWhenUnavailable)
      {
        Assert.Ignore(ex.Message);
        return;
      }

      _kernel = result.kernel;
      _resource = result.resource;
    }

    [OneTimeTearDown]
    public async Task TearDownAsync()
    {
      // Clear handles only AFTER successful disposal. If cleanup throws, the public
      // Resource/Kernel handles stay available for LastTeardownDiagnostics, retry, or
      // manual cleanup, and the exception propagates.
      await ResourceLifecycle.DisposeAsync(_resource!, _kernel!).ConfigureAwait(false);
      _resource = null;
      _kernel = null;
    }

    private void EnsureInitialized()
    {
      if (_resource == null)
        throw new InvalidOperationException(
            "Fixture has not been initialized. Call SetUpAsync first.");
    }
  }
}
