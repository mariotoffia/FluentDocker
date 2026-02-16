using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Kernel;
using FluentDocker.Testing.Core;

namespace FluentDocker.Testing.Xunit
{
  /// <summary>
  /// xUnit fixture that wraps a <see cref="TopologyResource"/>.
  /// Use with <c>IClassFixture&lt;XunitTopologyFixture&gt;</c> or
  /// <c>ICollectionFixture&lt;XunitTopologyFixture&gt;</c>.
  /// </summary>
  public class XunitTopologyFixture : IAsyncDisposable
  {
    private TopologyResource _resource;

    /// <summary>
    /// The underlying topology resource.
    /// </summary>
    public TopologyResource Resource => _resource;

    /// <summary>
    /// The kernel managing drivers.
    /// </summary>
    public FluentDockerKernel Kernel { get; private set; }

    /// <summary>
    /// Configures and initializes the fixture.
    /// </summary>
    /// <param name="configure">Builder configuration for the full topology.</param>
    /// <param name="kernelFactory">Optional kernel factory. Defaults to Docker CLI.</param>
    /// <param name="options">Optional resource options.</param>
    public async Task InitializeAsync(
        Action<Builder> configure,
        Func<Task<FluentDockerKernel>> kernelFactory = null,
        DockerResourceOptions options = null,
        CancellationToken cancellationToken = default)
    {
      if (_resource != null)
        throw new InvalidOperationException(
            "Fixture has already been initialized. Dispose before re-initializing.");

      var (kernel, resource) = await ResourceLifecycle.CreateAndInitializeAsync(
          k => new TopologyResource(k, configure, options),
          kernelFactory,
          cancellationToken: cancellationToken);

      Kernel = kernel;
      _resource = resource;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
      try
      {
        if (_resource != null)
          await _resource.DisposeAsync();
      }
      finally
      {
        if (Kernel != null)
          await Kernel.DisposeAsync();
        _resource = null;
        Kernel = null;
      }
    }
  }
}
