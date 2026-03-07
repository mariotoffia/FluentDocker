using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Kernel;
using FluentDocker.Testing.Core;
using Xunit;

namespace FluentDocker.Testing.Xunit
{
  /// <summary>
  /// Generic xUnit fixture for any <see cref="ITestResource"/>.
  /// Use this for plugin resources or custom resource types.
  /// Use with <c>IClassFixture&lt;XunitResourceFixture&lt;TResource&gt;&gt;</c> or
  /// <c>ICollectionFixture&lt;XunitResourceFixture&lt;TResource&gt;&gt;</c>.
  /// </summary>
  /// <remarks>
  /// <para>For container, compose, or topology resources, prefer the typed
  /// abstract fixture bases (<see cref="XunitContainerFixtureBase"/>,
  /// <see cref="XunitComposeFixtureBase"/>, <see cref="XunitTopologyFixtureBase"/>)
  /// which provide automatic async lifecycle via <c>IAsyncLifetime</c>.</para>
  /// <para>Use this class for custom or plugin resource types. Call
  /// <see cref="Configure"/> in your subclass constructor and let xUnit drive
  /// the lifecycle via <see cref="IAsyncLifetime"/>, or call the parameterized
  /// <see cref="InitializeAsync(Func{FluentDockerKernel,TResource},Func{Task{FluentDockerKernel}},CancellationToken)"/>
  /// directly for full manual control.</para>
  /// <para>Accessing properties before initialization throws
  /// <see cref="InvalidOperationException"/>.</para>
  /// </remarks>
  /// <typeparam name="TResource">The resource type to manage.</typeparam>
  public class XunitResourceFixture<TResource> : IAsyncLifetime
      where TResource : class, ITestResource
  {
    private TResource? _resource;
    private FluentDockerKernel? _kernel;
    private Func<FluentDockerKernel, TResource>? _deferredFactory;
    private Func<Task<FluentDockerKernel>>? _deferredKernelFactory;
    private bool _configured;

    /// <summary>
    /// The underlying resource.
    /// </summary>
    public TResource Resource
    {
      get { EnsureInitialized(); return _resource!; }
    }

    /// <summary>
    /// The kernel managing drivers.
    /// </summary>
    public FluentDockerKernel Kernel
    {
      get { EnsureInitialized(); return _kernel!; }
    }

    /// <summary>
    /// Stores configuration for deferred initialization via <see cref="IAsyncLifetime"/>.
    /// Call this in your fixture constructor, then let xUnit call
    /// <see cref="IAsyncLifetime.InitializeAsync"/>.
    /// </summary>
    /// <param name="resourceFactory">Factory receiving a kernel; returns the resource.</param>
    /// <param name="kernelFactory">Optional kernel factory. Defaults to Docker CLI.</param>
    /// <returns>This fixture for fluent chaining.</returns>
    public XunitResourceFixture<TResource> Configure(
        Func<FluentDockerKernel, TResource> resourceFactory,
        Func<Task<FluentDockerKernel>>? kernelFactory = null)
    {
      _deferredFactory = resourceFactory ?? throw new ArgumentNullException(nameof(resourceFactory));
      _deferredKernelFactory = kernelFactory;
      _configured = true;
      return this;
    }

    /// <summary>
    /// Called by xUnit when using <c>IClassFixture</c> or <c>ICollectionFixture</c>.
    /// Requires <see cref="Configure"/> to have been called first; otherwise throws.
    /// </summary>
    async ValueTask IAsyncLifetime.InitializeAsync()
    {
      if (_resource != null)
        return; // Already initialized via manual InitializeAsync() call

      if (!_configured)
        throw new InvalidOperationException(
            $"{GetType().Name} has not been configured. " +
            "Call Configure() in the fixture constructor.");
      await InitializeAsync(_deferredFactory!, _deferredKernelFactory);
    }

    /// <summary>
    /// Configures and initializes the fixture immediately.
    /// </summary>
    /// <param name="resourceFactory">Factory receiving a kernel; returns the resource.</param>
    /// <param name="kernelFactory">Optional kernel factory. Defaults to Docker CLI.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task InitializeAsync(
        Func<FluentDockerKernel, TResource> resourceFactory,
        Func<Task<FluentDockerKernel>>? kernelFactory = null,
        CancellationToken cancellationToken = default)
    {
      if (_resource != null)
        throw new InvalidOperationException(
            "Fixture has already been initialized. Dispose before re-initializing.");

      var (kernel, resource) = await ResourceLifecycle.CreateAndInitializeAsync(
          resourceFactory, kernelFactory,
          cancellationToken: cancellationToken);

      _kernel = kernel;
      _resource = resource;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
      try
      {
        await ResourceLifecycle.DisposeAsync(_resource, _kernel);
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
            "Fixture has not been initialized. Call InitializeAsync first.");
    }
  }
}
