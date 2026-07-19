#nullable disable warnings
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Model.Models;
using FluentDocker.Model.Models.Options;

namespace FluentDocker.Services
{
  /// <summary>
  /// Control-plane operations against the runner itself: status, version, list
  /// running models, load/unload, configure, logs and (engine-only) install.
  /// </summary>
  public interface IModelEngine
  {
    /// <summary>Reports the runner status.</summary>
    Task<ModelRunnerStatus> StatusAsync(CancellationToken cancellationToken = default);

    /// <summary>Reports the runner version.</summary>
    Task<ModelRunnerVersion> VersionAsync(CancellationToken cancellationToken = default);

    /// <summary>Lists running (loaded) models.</summary>
    Task<IReadOnlyList<RunningModel>> ListRunningAsync(CancellationToken cancellationToken = default);

    /// <summary>Loads (and optionally keeps resident) a model.</summary>
    Task LoadAsync(ModelReference model, ModelRunOptions options = null, CancellationToken cancellationToken = default);

    /// <summary>Unloads a single model.</summary>
    Task UnloadAsync(ModelReference model, CancellationToken cancellationToken = default);

    /// <summary>Unloads all currently-loaded models.</summary>
    Task UnloadAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Configures persistent per-model runtime settings.</summary>
    Task ConfigureAsync(ModelReference model, ModelConfigureOptions options, CancellationToken cancellationToken = default);

    /// <summary>Streams runner logs.</summary>
    IAsyncEnumerable<string> LogsAsync(bool follow = false, CancellationToken cancellationToken = default);

    /// <summary>Installs the runner (Docker Engine CE only).</summary>
    Task InstallRunnerAsync(ModelRunnerInstallOptions options = null, CancellationToken cancellationToken = default);

    /// <summary>Uninstalls the runner (Docker Engine CE only).</summary>
    Task UninstallRunnerAsync(ModelRunnerUninstallOptions options = null, CancellationToken cancellationToken = default);
  }
}
