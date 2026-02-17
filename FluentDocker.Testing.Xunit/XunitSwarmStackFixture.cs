using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Testing.Core;

namespace FluentDocker.Testing.Xunit
{
  /// <summary>
  /// xUnit fixture that wraps a <see cref="SwarmStackResource"/>.
  /// Use with <c>IClassFixture&lt;XunitSwarmStackFixture&gt;</c> or
  /// <c>ICollectionFixture&lt;XunitSwarmStackFixture&gt;</c>.
  /// </summary>
  /// <remarks>
  /// <para>This fixture requires explicit initialization via
  /// <see cref="InitializeAsync"/>. No abstract fixture base is provided for
  /// Swarm stacks because Swarm mode is not commonly used in unit tests.</para>
  /// <para>You <b>must</b> call <see cref="InitializeAsync"/> before accessing
  /// any properties — accessing them before initialization throws
  /// <see cref="InvalidOperationException"/>.</para>
  /// </remarks>
  public class XunitSwarmStackFixture : IAsyncDisposable
  {
    private SwarmStackResource _resource;
    private FluentDockerKernel _kernel;

    /// <summary>
    /// The underlying swarm stack resource.
    /// </summary>
    public SwarmStackResource Resource
    {
      get { EnsureInitialized(); return _resource; }
    }

    /// <summary>
    /// The stack name used for deployment.
    /// </summary>
    public string StackName
    {
      get { EnsureInitialized(); return _resource.StackName; }
    }

    /// <summary>
    /// The kernel managing drivers.
    /// </summary>
    public FluentDockerKernel Kernel
    {
      get { EnsureInitialized(); return _kernel; }
    }

    /// <summary>
    /// Configures and initializes the fixture.
    /// Call this from your fixture constructor or a setup method.
    /// </summary>
    /// <param name="config">Stack deploy configuration.</param>
    /// <param name="kernelFactory">Optional kernel factory. Defaults to Docker CLI.</param>
    /// <param name="options">Optional resource options.</param>
    public async Task InitializeAsync(
        StackDeployConfig config,
        Func<Task<FluentDockerKernel>> kernelFactory = null,
        DockerResourceOptions options = null,
        CancellationToken cancellationToken = default)
    {
      if (_resource != null)
        throw new InvalidOperationException(
            "Fixture has already been initialized. Dispose before re-initializing.");

      var (kernel, resource) = await ResourceLifecycle.CreateAndInitializeAsync(
          k => new SwarmStackResource(k, config, options),
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
