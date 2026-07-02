using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
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
    public async Task UnloadAsync(ModelReference model, CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      ArgumentNullException.ThrowIfNull(model);
      var response = await Runtime().UnloadAsync(Context(), model, cancellationToken).ConfigureAwait(false);
      UnwrapUnit(response, $"Unload model '{model}'");
    }

    /// <inheritdoc />
    public async Task UnloadAllAsync(CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      var response = await Runtime().UnloadAllAsync(Context(), cancellationToken).ConfigureAwait(false);
      UnwrapUnit(response, "Unload all models");
    }

    /// <inheritdoc />
    public async Task ConfigureAsync(ModelReference model, ModelConfigureOptions options, CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      // Serialize configure on the SAME per-model gate that build-time pull/load/unload use, so a
      // concurrent gated op (or another configure) for THIS model cannot stomp its persistent
      // config mid-flight. The gate is keyed per model, so different models still run in parallel.
      // Gating this shared method also covers the build-time configure in ModelRunnerBuilder, which
      // routes through here — do NOT add a second gate at that call site (the semaphore is
      // non-reentrant, so double-acquiring the same key would deadlock).
      await using var gate = await ModelOperationGate.AcquireAsync(model, cancellationToken).ConfigureAwait(false);
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

    /// <inheritdoc />
    public async Task UninstallRunnerAsync(ModelRunnerUninstallOptions options = null, CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      var response = await Runtime().UninstallRunnerAsync(Context(), options, cancellationToken).ConfigureAwait(false);
      UnwrapUnit(response, "Uninstall runner");
    }
  }
}
