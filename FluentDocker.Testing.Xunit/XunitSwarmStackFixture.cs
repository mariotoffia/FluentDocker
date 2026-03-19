using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Testing.Core;
using Xunit;

namespace FluentDocker.Testing.Xunit
{
  /// <summary>
  /// xUnit fixture that wraps a <see cref="SwarmStackResource"/>.
  /// Use with <c>IClassFixture&lt;XunitSwarmStackFixture&gt;</c> or
  /// <c>ICollectionFixture&lt;XunitSwarmStackFixture&gt;</c>.
  /// </summary>
  /// <remarks>
  /// <para>This fixture requires explicit configuration via <see cref="Configure"/>
  /// (recommended) or <see cref="InitializeAsync(StackDeployConfig,Func{Task{FluentDockerKernel}},DockerResourceOptions,CancellationToken)"/>
  /// (manual). No abstract fixture base is provided for Swarm stacks because
  /// Swarm mode is not commonly used in unit tests.</para>
  /// <para>Accessing properties before initialization throws
  /// <see cref="InvalidOperationException"/>.</para>
  /// </remarks>
  public class XunitSwarmStackFixture : IAsyncLifetime
  {
    private SwarmStackResource? _resource;
    private FluentDockerKernel? _kernel;
    private StackDeployConfig? _deferredConfig;
    private Func<Task<FluentDockerKernel>>? _deferredKernelFactory;
    private DockerResourceOptions? _deferredOptions;
    private bool _configured;

    /// <summary>
    /// The underlying swarm stack resource.
    /// </summary>
    public SwarmStackResource Resource
    {
      get { EnsureInitialized(); return _resource!; }
    }

    /// <summary>
    /// The stack name used for deployment.
    /// </summary>
    public string StackName
    {
      get { EnsureInitialized(); return _resource!.StackName; }
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
    /// <param name="config">Stack deploy configuration.</param>
    /// <param name="kernelFactory">Optional kernel factory. Defaults to Docker CLI.</param>
    /// <param name="options">Optional resource options.</param>
    /// <returns>This fixture for fluent chaining.</returns>
    public XunitSwarmStackFixture Configure(
        StackDeployConfig config,
        Func<Task<FluentDockerKernel>>? kernelFactory = null,
        DockerResourceOptions? options = null)
    {
      _deferredConfig = config ?? throw new ArgumentNullException(nameof(config));
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
            "Call Configure() in the fixture constructor.");
      await InitializeAsync(_deferredConfig!, _deferredKernelFactory, _deferredOptions);
    }

    /// <summary>
    /// Configures and initializes the fixture immediately.
    /// </summary>
    public async Task InitializeAsync(
        StackDeployConfig config,
        Func<Task<FluentDockerKernel>>? kernelFactory = null,
        DockerResourceOptions? options = null,
        CancellationToken cancellationToken = default)
    {
      if (_resource != null)
        throw new InvalidOperationException(
            "Fixture has already been initialized. Dispose before re-initializing.");

      var (kernel, resource) = await ResourceLifecycle.CreateAndInitializeAsync(
          k => new SwarmStackResource(k, config, options!),
          kernelFactory!,
          cancellationToken: cancellationToken);

      _kernel = kernel;
      _resource = resource;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
      try
      {
        await ResourceLifecycle.DisposeAsync(_resource!, _kernel!);
      }
      finally
      {
        _resource = null;
        _kernel = null;
      }

      GC.SuppressFinalize(this);
    }

    private void EnsureInitialized()
    {
      if (_resource == null)
        throw new InvalidOperationException(
            "Fixture has not been initialized. Call InitializeAsync first.");
    }
  }
}
