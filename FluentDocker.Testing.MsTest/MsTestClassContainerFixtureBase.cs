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
  /// MSTest base class for a container shared by all test methods in one test class.
  /// </summary>
  /// <typeparam name="TFixture">The concrete derived test class type.</typeparam>
  /// <remarks>
  /// MSTest lifecycle hooks are static for class cleanup, so the concrete type is
  /// part of the generic base to keep one shared container per derived test class.
  /// The derived class must call <see cref="CleanupClassAsync"/> from
  /// <c>[ClassCleanup(ClassCleanupBehavior.EndOfClass)]</c>; MSTest 3.x defaults
  /// a bare <c>[ClassCleanup]</c> to end-of-assembly cleanup, which keeps every
  /// class-scoped container alive until the whole test assembly finishes.
  /// Pass the most-derived class as <typeparamref name="TFixture"/>. If class
  /// <c>B</c> derives from class <c>A</c> and both close this base as <c>A</c>,
  /// they share the same static container and cleanup state.
  /// Use <see cref="MsTestContainerFixtureBase"/> when each test method should get
  /// a fresh container.
  /// </remarks>
  public abstract class MsTestClassContainerFixtureBase<TFixture>
      where TFixture : MsTestClassContainerFixtureBase<TFixture>
  {
    private static readonly SemaphoreSlim LifecycleLock = new(1, 1);
    private static ContainerResource? _resource;
    private static FluentDockerKernel? _kernel;

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
    /// Override to configure the container. Called once for the test class.
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
    /// Initializes the shared class container on the first test method.
    /// </summary>
    [TestInitialize]
    public async Task TestInitializeAsync()
    {
      if (_resource != null)
        return;

      await LifecycleLock.WaitAsync().ConfigureAwait(false);
      try
      {
        if (_resource != null)
          return;

        var (kernel, resource) = await ResourceLifecycle.CreateAndInitializeAsync(
            k => new ContainerResource(k, ConfigureContainer, GetOptions()!),
            KernelFactory!).ConfigureAwait(false);

        _kernel = kernel;
        _resource = resource;
      }
      finally
      {
        LifecycleLock.Release();
      }
    }

    /// <summary>
    /// Disposes the shared class container from the derived class's
    /// <c>[ClassCleanup]</c> method.
    /// </summary>
    protected static async Task CleanupClassAsync()
    {
      await LifecycleLock.WaitAsync().ConfigureAwait(false);
      try
      {
        await ResourceLifecycle.DisposeAsync(_resource!, _kernel!).ConfigureAwait(false);
        _resource = null;
        _kernel = null;
      }
      finally
      {
        LifecycleLock.Release();
      }
    }

    private static void EnsureInitialized()
    {
      if (_resource == null)
        throw new InvalidOperationException(
            "Fixture has not been initialized. Call TestInitializeAsync first.");
    }
  }
}
