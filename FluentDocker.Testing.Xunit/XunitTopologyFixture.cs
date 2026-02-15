using System;
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
        DockerResourceOptions options = null)
    {
      Kernel = kernelFactory != null
          ? await kernelFactory()
          : await FluentDockerKernel.Create()
              .WithDockerCli("docker-cli", d => d.AsDefault())
              .BuildAsync();

      _resource = new TopologyResource(Kernel, configure, options);
      await _resource.InitializeAsync();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
      if (_resource != null)
        await _resource.DisposeAsync();

      Kernel?.Dispose();
    }
  }
}
