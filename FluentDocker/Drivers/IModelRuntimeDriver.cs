using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Models;
using FluentDocker.Model.Models.Options;

namespace FluentDocker.Drivers
{
  /// <summary>
  /// Hexagonal port for runner control-plane operations (status/version/ps/
  /// load/unload/configure/logs/install). Backed by the CLI adapter
  /// (<c>docker model …</c>). Returns <see cref="CommandResponse{T}"/>; logs stream
  /// as <see cref="IAsyncEnumerable{T}"/>.
  /// </summary>
  public interface IModelRuntimeDriver
  {
    /// <summary>Reports the runner status.</summary>
    /// <param name="context">The driver context.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The runner status.</returns>
    Task<CommandResponse<ModelRunnerStatus>> StatusAsync(DriverContext context,
        CancellationToken cancellationToken = default);

    /// <summary>Reports the runner version.</summary>
    /// <param name="context">The driver context.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The runner version.</returns>
    Task<CommandResponse<ModelRunnerVersion>> VersionAsync(DriverContext context,
        CancellationToken cancellationToken = default);

    /// <summary>Lists running (loaded) models.</summary>
    /// <param name="context">The driver context.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The running models.</returns>
    Task<CommandResponse<IList<RunningModel>>> ListRunningAsync(DriverContext context,
        CancellationToken cancellationToken = default);

    /// <summary>Loads (and optionally keeps resident) a model.</summary>
    /// <param name="context">The driver context.</param>
    /// <param name="model">The model reference.</param>
    /// <param name="options">Run options.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A unit response.</returns>
    /// <exception cref="System.ArgumentNullException"><paramref name="model"/> is null.</exception>
    Task<CommandResponse<Unit>> LoadAsync(DriverContext context,
        ModelReference model, ModelRunOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>Unloads a single model.</summary>
    /// <param name="context">The driver context.</param>
    /// <param name="model">The model reference.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A unit response.</returns>
    /// <exception cref="System.ArgumentNullException"><paramref name="model"/> is null.</exception>
    Task<CommandResponse<Unit>> UnloadAsync(DriverContext context,
        ModelReference model,
        CancellationToken cancellationToken = default);

    /// <summary>Unloads all currently-loaded models.</summary>
    /// <param name="context">The driver context.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A unit response.</returns>
    Task<CommandResponse<Unit>> UnloadAllAsync(DriverContext context,
        CancellationToken cancellationToken = default);

    /// <summary>Configures persistent per-model runtime settings.</summary>
    /// <param name="context">The driver context.</param>
    /// <param name="model">The model reference.</param>
    /// <param name="options">The configuration options.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A unit response.</returns>
    /// <exception cref="System.ArgumentNullException"><paramref name="model"/> is null.</exception>
    Task<CommandResponse<Unit>> ConfigureAsync(DriverContext context,
        ModelReference model, ModelConfigureOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>Streams runner logs.</summary>
    /// <param name="context">The driver context.</param>
    /// <param name="follow">Whether to follow (tail) the logs.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>An async stream of log lines.</returns>
    IAsyncEnumerable<string> LogsAsync(DriverContext context, bool follow = false,
        CancellationToken cancellationToken = default);

    /// <summary>Installs the runner (Docker Engine CE only).</summary>
    /// <param name="context">The driver context.</param>
    /// <param name="options">Install options.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A unit response.</returns>
    Task<CommandResponse<Unit>> InstallRunnerAsync(DriverContext context,
        ModelRunnerInstallOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>Uninstalls the runner (Docker Engine CE only).</summary>
    /// <param name="context">The driver context.</param>
    /// <param name="options">Uninstall options.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A unit response.</returns>
    Task<CommandResponse<Unit>> UninstallRunnerAsync(DriverContext context,
        ModelRunnerUninstallOptions? options = null,
        CancellationToken cancellationToken = default);
  }
}
