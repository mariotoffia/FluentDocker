using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Kernel;
using FluentDocker.Services;
using FluentDocker.Testing.Core;
using Xunit;

namespace FluentDocker.Testing.Xunit
{
  /// <summary>
  /// xUnit fixture that wraps a <see cref="ComposeResource"/>.
  /// Use with <c>IClassFixture&lt;XunitComposeFixture&gt;</c> or
  /// <c>ICollectionFixture&lt;XunitComposeFixture&gt;</c>.
  /// </summary>
  /// <remarks>
  /// <para><b>Prefer <see cref="XunitComposeFixtureBase"/></b> for most use cases.
  /// Subclass it and override <see cref="XunitComposeFixtureBase.ConfigureCompose"/>
  /// — xUnit handles the async lifecycle automatically.</para>
  /// <para>Use this class when you need programmatic control over initialization.
  /// Call <see cref="Configure"/> in your subclass constructor and let xUnit drive
  /// the lifecycle via <see cref="IAsyncLifetime"/>, or call the parameterized
  /// <see cref="InitializeAsync(Action{IComposeBuilder},Func{Task{FluentDockerKernel}},DockerResourceOptions,CancellationToken)"/>
  /// directly for full manual control.</para>
  /// <para>Accessing properties before initialization throws
  /// <see cref="InvalidOperationException"/>.</para>
  /// </remarks>
  public class XunitComposeFixture : IAsyncLifetime
  {
    private ComposeResource? _resource;
    private FluentDockerKernel? _kernel;
    private Action<IComposeBuilder>? _deferredConfigure;
    private Func<Task<FluentDockerKernel>>? _deferredKernelFactory;
    private DockerResourceOptions? _deferredOptions;
    private bool _configured;

    /// <summary>
    /// The underlying compose resource.
    /// </summary>
    public ComposeResource Resource
    {
      get { EnsureInitialized(); return _resource!; }
    }

    /// <summary>
    /// The running compose service, available after initialization.
    /// </summary>
    public IComposeService Service
    {
      get { EnsureInitialized(); return _resource!.Service; }
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
    /// <param name="configure">Compose builder configuration.</param>
    /// <param name="kernelFactory">Optional kernel factory. Defaults to Docker CLI.</param>
    /// <param name="options">Optional resource options.</param>
    /// <returns>This fixture for fluent chaining.</returns>
    public XunitComposeFixture Configure(
        Action<IComposeBuilder> configure,
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
      if (_resource != null)
        return; // Already initialized via manual InitializeAsync() call

      if (!_configured)
        throw new InvalidOperationException(
            $"{GetType().Name} has not been configured. " +
            "Call Configure() in the fixture constructor, or use " +
            "XunitComposeFixtureBase instead.");
      await InitializeAsync(_deferredConfigure!, _deferredKernelFactory, _deferredOptions);
    }

    /// <summary>
    /// Configures and initializes the fixture immediately.
    /// </summary>
    public async Task InitializeAsync(
        Action<IComposeBuilder> configure,
        Func<Task<FluentDockerKernel>>? kernelFactory = null,
        DockerResourceOptions? options = null,
        CancellationToken cancellationToken = default)
    {
      if (_resource != null)
        throw new InvalidOperationException(
            "Fixture has already been initialized. Dispose before re-initializing.");

      var (kernel, resource) = await ResourceLifecycle.CreateAndInitializeAsync(
          k => new ComposeResource(k, configure, options),
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
