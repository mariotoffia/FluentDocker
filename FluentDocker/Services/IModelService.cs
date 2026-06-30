using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Model.Models;
using FluentDocker.Model.Models.Options;

namespace FluentDocker.Services
{
  /// <summary>
  /// A handle to a single loaded model, participating in the same
  /// <see cref="IServiceAsync"/> lifecycle (state machine + hooks) as containers
  /// and volumes. <c>StartAsync</c> loads, <c>StopAsync</c> unloads,
  /// <c>RemoveAsync</c> removes.
  /// </summary>
  public interface IModelService : IServiceAsync
  {
    /// <summary>The model this service manages.</summary>
    ModelReference Model { get; }

    /// <summary>Inspects the model.</summary>
    Task<ModelInfo> InspectAsync(CancellationToken cancellationToken = default);

    /// <summary>Configures persistent per-model runtime settings.</summary>
    Task ConfigureAsync(ModelConfigureOptions options, CancellationToken cancellationToken = default);

    /// <summary>An <see cref="IModelRunner"/> bound to this model for inference.</summary>
    IModelRunner Runner { get; }

    /// <summary>
    /// When true, the model is left loaded (NOT unloaded) when the service is disposed.
    /// Disposal still releases the owned runner; only the unload is skipped.
    /// </summary>
    bool KeepRunning { get; }
  }
}
