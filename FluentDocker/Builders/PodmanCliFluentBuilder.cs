using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Kernel;
using FluentDocker.Services;

namespace FluentDocker.Builders
{
  /// <summary>
  /// Type-safe fluent builder for Podman CLI operations.
  /// Exposes common operations plus Podman-specific features like pods.
  /// </summary>
  public class PodmanCliFluentBuilder
  {
    private readonly Builder _inner;

    internal PodmanCliFluentBuilder(Builder inner)
    {
      ArgumentNullException.ThrowIfNull(inner);
      _inner = inner;
    }

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
    /// Enters the model runner fluent builder to manage local models and run inference.
    /// </summary>
    /// <remarks>
    /// The Podman CLI pack registers no model ports today; use a model-capable pack or the
    /// standard interface-not-supported error is surfaced.
    /// </remarks>
    /// <returns>A model runner builder.</returns>
    public IModelRunnerBuilder UseModelRunner() => _inner.UseModelRunner();

    /// <summary>
    /// Begins building a managed single-model <see cref="IModelService"/> in the current Podman CLI scope.
    /// </summary>
    /// <remarks>
    /// The Podman CLI pack registers no model ports today; use a model-capable pack or the
    /// standard interface-not-supported error is surfaced.
    /// </remarks>
    /// <param name="reference">The model reference.</param>
    /// <returns>A model service builder.</returns>
    public IModelServiceBuilder UseModel(string reference) => _inner.UseModel(reference);

    /// <summary>
    /// Begins building a managed single-model <see cref="IModelService"/> from a pre-built <see cref="Model.Models.ModelReference"/>.
    /// </summary>
    /// <remarks>
    /// The Podman CLI pack registers no model ports today; use a model-capable pack or the
    /// standard interface-not-supported error is surfaced.
    /// </remarks>
    /// <param name="reference">The model reference.</param>
    /// <returns>A model service builder.</returns>
    public IModelServiceBuilder UseModel(Model.Models.ModelReference reference) => _inner.UseModel(reference);

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
