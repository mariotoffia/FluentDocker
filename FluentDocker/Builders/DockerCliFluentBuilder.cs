using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Kernel;
using FluentDocker.Model.Kernel;

namespace FluentDocker.Builders
{
  /// <summary>
  /// Type-safe fluent builder for Docker CLI operations.
  /// Exposes common operations plus Docker CLI-specific features like Compose.
  /// </summary>
  public class DockerCliFluentBuilder
  {
    private readonly Builder _inner;

    internal DockerCliFluentBuilder(Builder inner) => _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    /// <summary>
    /// Adds a container operation.
    /// </summary>
    public DockerCliFluentBuilder UseContainer(Action<IContainerBuilder> configure)
    {
      _inner.UseContainer(configure);
      return this;
    }

    /// <summary>
    /// Adds a network operation.
    /// </summary>
    public DockerCliFluentBuilder UseNetwork(Action<INetworkBuilder> configure)
    {
      _inner.UseNetwork(configure);
      return this;
    }

    /// <summary>
    /// Adds a volume operation.
    /// </summary>
    public DockerCliFluentBuilder UseVolume(Action<IVolumeBuilder> configure)
    {
      _inner.UseVolume(configure);
      return this;
    }

    /// <summary>
    /// Adds an image build operation.
    /// </summary>
    public DockerCliFluentBuilder UseImage(string imageName, Action<DockerfileBuilder> configure)
    {
      _inner.UseImage(imageName, configure);
      return this;
    }

    /// <summary>
    /// Adds a Docker Compose operation (Docker CLI-specific).
    /// Uses the Compose V2 "docker compose" subcommand.
    /// </summary>
    public DockerCliFluentBuilder UseCompose(Action<IComposeBuilder> configure)
    {
      _inner.UseCompose(configure);
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
