using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Model.Models;
using FluentDocker.Model.Models.Options;

namespace FluentDocker.Services.Impl
{
  public sealed partial class ModelRunnerService
  {
    /// <inheritdoc />
    public async Task<ModelRunnerStatus> StatusAsync(CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      var response = await Runtime().StatusAsync(Context(), cancellationToken).ConfigureAwait(false);
      return Unwrap(response, "Runner status");
    }

    /// <inheritdoc />
    public async Task<ModelRunnerVersion> VersionAsync(CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      var response = await Runtime().VersionAsync(Context(), cancellationToken).ConfigureAwait(false);
      return Unwrap(response, "Runner version");
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RunningModel>> ListRunningAsync(CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      var response = await Runtime().ListRunningAsync(Context(), cancellationToken).ConfigureAwait(false);
      return ToReadOnly(Unwrap(response, "List running models"));
    }

    /// <inheritdoc />
    public async Task LoadAsync(ModelReference model, ModelRunOptions options = null, CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      var response = await Runtime().LoadAsync(Context(), model, options, cancellationToken).ConfigureAwait(false);
      UnwrapUnit(response, $"Load model '{model}'");
    }

    /// <inheritdoc />
    public async Task UnloadAsync(ModelReference model, bool all = false, CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      var response = await Runtime().UnloadAsync(Context(), model, all, cancellationToken).ConfigureAwait(false);
      UnwrapUnit(response, all ? "Unload all models" : $"Unload model '{model}'");
    }

    /// <inheritdoc />
    public async Task ConfigureAsync(ModelReference model, ModelConfigureOptions options, CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      var response = await Runtime().ConfigureAsync(Context(), model, options, cancellationToken).ConfigureAwait(false);
      UnwrapUnit(response, $"Configure model '{model}'");
    }

    /// <inheritdoc />
    public IAsyncEnumerable<string> LogsAsync(bool follow = false, CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      return Runtime().LogsAsync(Context(), follow, cancellationToken);
    }

    /// <inheritdoc />
    public async Task InstallRunnerAsync(ModelRunnerInstallOptions options = null, CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      var response = await Runtime().InstallRunnerAsync(Context(), options, cancellationToken).ConfigureAwait(false);
      UnwrapUnit(response, "Install runner");
    }
  }
}
