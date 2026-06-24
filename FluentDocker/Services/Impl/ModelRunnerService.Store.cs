using System;
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
    public async Task<ModelInfo> PullAsync(ModelReference model, IProgress<ModelPullProgress> progress = null,
        CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      var response = await Management().PullAsync(Context(), model, progress, cancellationToken).ConfigureAwait(false);
      return Unwrap(response, $"Pull model '{model}'");
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ModelInfo>> ListAsync(CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      var response = await Management().ListAsync(Context(), cancellationToken).ConfigureAwait(false);
      return ToReadOnly(Unwrap(response, "List models"));
    }

    /// <inheritdoc />
    public async Task<ModelInfo> InspectAsync(ModelReference model, CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      var response = await Management().InspectAsync(Context(), model, cancellationToken).ConfigureAwait(false);
      return Unwrap(response, $"Inspect model '{model}'");
    }

    /// <inheritdoc />
    public async Task RemoveAsync(ModelReference model, bool force = false, CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      var response = await Management().RemoveAsync(Context(), model, force, cancellationToken).ConfigureAwait(false);
      UnwrapUnit(response, $"Remove model '{model}'");
    }

    /// <inheritdoc />
    public async Task TagAsync(ModelReference source, ModelReference target, CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      var response = await Management().TagAsync(Context(), source, target, cancellationToken).ConfigureAwait(false);
      UnwrapUnit(response, $"Tag model '{source}' as '{target}'");
    }

    /// <inheritdoc />
    public async Task PushAsync(ModelReference model, CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      var response = await Management().PushAsync(Context(), model, cancellationToken).ConfigureAwait(false);
      UnwrapUnit(response, $"Push model '{model}'");
    }

    /// <inheritdoc />
    public async Task<ModelInfo> PackageAsync(ModelPackageRequest request, CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      var response = await Management().PackageAsync(Context(), request, cancellationToken).ConfigureAwait(false);
      return Unwrap(response, "Package model");
    }

    /// <summary>Removes ALL locally-stored models (irreversible) — maps to <c>docker model purge --force</c>.</summary>
    public async Task<ModelPruneResult> PurgeAllAsync(CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      var response = await Management().PurgeAllAsync(Context(), cancellationToken).ConfigureAwait(false);
      return Unwrap(response, "Prune models");
    }

    /// <inheritdoc />
    public async Task<ModelDiskUsage> DiskUsageAsync(CancellationToken cancellationToken = default)
    {
      ThrowIfDisposed();
      var response = await Management().DiskUsageAsync(Context(), cancellationToken).ConfigureAwait(false);
      return Unwrap(response, "Model disk usage");
    }
  }
}
