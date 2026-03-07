using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Kernel;
using FluentDocker.Testing.Core;
using Xunit;

namespace FluentDocker.Testing.Xunit
{
  /// <summary>
  /// xUnit fixture that wraps a <see cref="TopologyResource"/>.
  /// Use with <c>IClassFixture&lt;XunitTopologyFixture&gt;</c> or
  /// <c>ICollectionFixture&lt;XunitTopologyFixture&gt;</c>.
  /// </summary>
  /// <remarks>
  /// <para><b>Prefer <see cref="XunitTopologyFixtureBase"/></b> for most use cases.
  /// Subclass it and override <see cref="XunitTopologyFixtureBase.ConfigureTopology"/>
  /// — xUnit handles the async lifecycle automatically.</para>
  /// <para>Use this class when you need programmatic control over initialization.
  /// Call <see cref="Configure"/> in your subclass constructor and let xUnit drive
  /// the lifecycle via <see cref="IAsyncLifetime"/>, or call the parameterized
  /// <see cref="InitializeAsync(Action{Builder},Func{Task{FluentDockerKernel}},DockerResourceOptions,CancellationToken)"/>
  /// directly for full manual control.</para>
  /// <para>Accessing properties before initialization throws
  /// <see cref="InvalidOperationException"/>.</para>
  /// </remarks>
  public class XunitTopologyFixture : IAsyncLifetime
  {
    private TopologyResource? _resource;
    private FluentDockerKernel? _kernel;
    private Action<Builder>? _deferredConfigure;
    private Func<Task<FluentDockerKernel>>? _deferredKernelFactory;
    private DockerResourceOptions? _deferredOptions;
    private bool _configured;

    /// <summary>
    /// The underlying topology resource.
    /// </summary>
    public TopologyResource Resource
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
    /// <param name="configure">Builder configuration for the full topology.</param>
    /// <param name="kernelFactory">Optional kernel factory. Defaults to Docker CLI.</param>
    /// <param name="options">Optional resource options.</param>
    /// <returns>This fixture for fluent chaining.</returns>
    public XunitTopologyFixture Configure(
        Action<Builder> configure,
        Func<Task<FluentDockerKernel>>? kernelFactory = null,
        DockerResourceOptions? options = null)
    {
      _deferredConfigure = configure ?? throw new ArgumentNullException(nameof(configure));
      _deferredKernelFactory = kernelFactory;
      _deferredOptions = options;
      _configured = true;
      return this;
    }

    /// <summary>
    /// Called by xUnit when using <c>IClassFixture</c> or <c>ICollectionFixture</c>.
    /// Requires <see cref="Configure"/> to have been called first; otherwise throws.
    /// </summary>
    async ValueTask IAsyncLifetime.InitializeAsync()
    {
      if (!_configured)
        throw new InvalidOperationException(
            $"{GetType().Name} has not been configured. " +
            "Call Configure() in the fixture constructor, or use " +
            "XunitTopologyFixtureBase instead.");
      await InitializeAsync(_deferredConfigure!, _deferredKernelFactory, _deferredOptions);
    }

    /// <summary>
    /// Configures and initializes the fixture immediately.
    /// </summary>
    public async Task InitializeAsync(
        Action<Builder> configure,
        Func<Task<FluentDockerKernel>>? kernelFactory = null,
        DockerResourceOptions? options = null,
        CancellationToken cancellationToken = default)
    {
      if (_resource != null)
        throw new InvalidOperationException(
            "Fixture has already been initialized. Dispose before re-initializing.");

      var (kernel, resource) = await ResourceLifecycle.CreateAndInitializeAsync(
          k => new TopologyResource(k, configure, options),
          kernelFactory,
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
