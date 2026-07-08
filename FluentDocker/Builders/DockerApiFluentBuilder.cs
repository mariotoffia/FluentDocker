using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Kernel;

namespace FluentDocker.Builders
{
  /// <summary>
  /// Type-safe fluent builder for Docker API operations.
  /// Exposes only common operations supported by the Docker Engine REST API.
  /// </summary>
  public class DockerApiFluentBuilder
  {
    private readonly Builder _inner;
    private readonly FluentDockerKernel _kernel;
    private readonly string _driverId;

    internal DockerApiFluentBuilder(Builder inner, FluentDockerKernel kernel, string driverId)
    {
      ArgumentNullException.ThrowIfNull(inner);
      _inner = inner;
      _kernel = kernel;
      _driverId = driverId;
    }

    /// <summary>
    /// Adds a container operation.
    /// </summary>
    public DockerApiFluentBuilder UseContainer(Action<IContainerBuilder> configure)
    {
      _inner.RunInScope(_kernel, _driverId, () => _inner.UseContainer(configure));
      return this;
    }

    /// <summary>
    /// Adds a network operation.
    /// </summary>
    public DockerApiFluentBuilder UseNetwork(Action<INetworkBuilder> configure)
    {
      _inner.RunInScope(_kernel, _driverId, () => _inner.UseNetwork(configure));
      return this;
    }

    /// <summary>
    /// Adds a volume operation.
    /// </summary>
    public DockerApiFluentBuilder UseVolume(Action<IVolumeBuilder> configure)
    {
      _inner.RunInScope(_kernel, _driverId, () => _inner.UseVolume(configure));
      return this;
    }

    /// <summary>
    /// Adds an image build operation.
    /// </summary>
    public DockerApiFluentBuilder UseImage(string imageName, Action<DockerfileBuilder> configure)
    {
      _inner.RunInScope(_kernel, _driverId, () => _inner.UseImage(imageName, configure));
      return this;
    }

    /// <summary>
    /// TERMINAL - Builds all operations synchronously.
    /// For async contexts, prefer <see cref="BuildAsync"/>.
    /// </summary>
    public BuildResults Build() => _inner.Build();

    /// <summary>
    /// TERMINAL - Builds all operations asynchronously.
    /// </summary>
    public Task<BuildResults> BuildAsync(
        TimeSpan? cleanupTimeout = null,
        CancellationToken cancellationToken = default)
        => _inner.BuildAsync(cleanupTimeout, cancellationToken);
  }
}
