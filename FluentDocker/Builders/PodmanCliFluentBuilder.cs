using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Kernel;
using FluentDocker.Model.Kernel;

namespace FluentDocker.Builders
{
  /// <summary>
  /// Type-safe fluent builder for Podman CLI operations.
  /// Exposes common operations plus Podman-specific features like pods.
  /// </summary>
  public class PodmanCliFluentBuilder
  {
    private readonly Builder _inner;

    internal PodmanCliFluentBuilder(Builder inner) => _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    /// <summary>
    /// Adds a container operation.
    /// </summary>
    public PodmanCliFluentBuilder UseContainer(Action<IContainerBuilder> configure)
    {
      _inner.UseContainer(configure);
      return this;
    }

    /// <summary>
    /// Adds a network operation.
    /// </summary>
    public PodmanCliFluentBuilder UseNetwork(Action<INetworkBuilder> configure)
    {
      _inner.UseNetwork(configure);
      return this;
    }

    /// <summary>
    /// Adds a volume operation.
    /// </summary>
    public PodmanCliFluentBuilder UseVolume(Action<IVolumeBuilder> configure)
    {
      _inner.UseVolume(configure);
      return this;
    }

    /// <summary>
    /// Adds an image build operation.
    /// </summary>
    public PodmanCliFluentBuilder UseImage(string imageName, Action<DockerfileBuilder> configure)
    {
      _inner.UseImage(imageName, configure);
      return this;
    }

    /// <summary>
    /// Adds a Podman pod operation (Podman CLI-specific).
    /// Creates and configures a pod that containers can join.
    /// </summary>
    public PodmanCliFluentBuilder UsePod(Action<IPodBuilder> configure)
    {
      _inner.UsePod(configure);
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
    public Task<BuildResults> BuildAsync(CancellationToken cancellationToken = default)
        => _inner.BuildAsync(cancellationToken);
  }
}
