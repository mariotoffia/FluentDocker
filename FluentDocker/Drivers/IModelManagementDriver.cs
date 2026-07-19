using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Models;
using FluentDocker.Model.Models.Options;

namespace FluentDocker.Drivers
{
  /// <summary>
  /// Hexagonal port for model distribution and local-store operations
  /// (pull/list/inspect/remove/tag/push/package/prune/disk-usage). Implemented by
  /// CLI adapters (<c>docker model …</c>). Returns <see cref="CommandResponse{T}"/>
  /// — never throws for expected
  /// failures; the service layer translates failures into exceptions.
  /// </summary>
  public interface IModelManagementDriver
  {
    /// <summary>Pulls a model, reporting progress.</summary>
    /// <param name="context">The driver context.</param>
    /// <param name="model">The model reference to pull.</param>
    /// <param name="progress">An optional progress sink.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The pulled model's info.</returns>
    /// <exception cref="System.ArgumentNullException"><paramref name="model"/> is null.</exception>
    Task<CommandResponse<ModelInfo>> PullAsync(DriverContext context,
        ModelReference model, IProgress<ModelPullProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>Lists local models.</summary>
    /// <param name="context">The driver context.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The local models.</returns>
    Task<CommandResponse<IList<ModelInfo>>> ListAsync(DriverContext context,
        CancellationToken cancellationToken = default);

    /// <summary>Inspects a local model.</summary>
    /// <param name="context">The driver context.</param>
    /// <param name="model">The model reference.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The model info.</returns>
    /// <exception cref="System.ArgumentNullException"><paramref name="model"/> is null.</exception>
    Task<CommandResponse<ModelInfo>> InspectAsync(DriverContext context,
        ModelReference model, CancellationToken cancellationToken = default);

    /// <summary>Removes a local model.</summary>
    /// <param name="context">The driver context.</param>
    /// <param name="model">The model reference.</param>
    /// <param name="force">Whether to force removal.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A unit response.</returns>
    /// <exception cref="System.ArgumentNullException"><paramref name="model"/> is null.</exception>
    Task<CommandResponse<Unit>> RemoveAsync(DriverContext context,
        ModelReference model, bool force = false,
        CancellationToken cancellationToken = default);

    /// <summary>Tags a model under a new reference.</summary>
    /// <param name="context">The driver context.</param>
    /// <param name="source">The source reference.</param>
    /// <param name="target">The target reference.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A unit response.</returns>
    /// <exception cref="System.ArgumentNullException">
    /// <paramref name="source"/> or <paramref name="target"/> is null.
    /// </exception>
    Task<CommandResponse<Unit>> TagAsync(DriverContext context,
        ModelReference source, ModelReference target,
        CancellationToken cancellationToken = default);

    /// <summary>Pushes a model to its registry.</summary>
    /// <param name="context">The driver context.</param>
    /// <param name="model">The model reference.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A unit response.</returns>
    /// <exception cref="System.ArgumentNullException"><paramref name="model"/> is null.</exception>
    Task<CommandResponse<Unit>> PushAsync(DriverContext context,
        ModelReference model, CancellationToken cancellationToken = default);

    /// <summary>Packages a GGUF file into an OCI model artifact.</summary>
    /// <param name="context">The driver context.</param>
    /// <param name="request">The package request.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// Sparse info for the produced artifact. The CLI does not return inspect data here;
    /// <see cref="ModelInfo.Reference"/> mirrors <see cref="ModelPackageRequest.Target"/>
    /// and may be <c>null</c> when the request omitted a target.
    /// </returns>
    /// <exception cref="System.ArgumentNullException"><paramref name="request"/> is null.</exception>
    Task<CommandResponse<ModelInfo>> PackageAsync(DriverContext context,
        ModelPackageRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes ALL models from the local store (<c>docker model purge --force</c>). DMR
    /// exposes only a purge-all operation; there is no unused-only mode.
    /// </summary>
    /// <param name="context">The driver context.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The prune result.</returns>
    Task<CommandResponse<ModelPruneResult>> PurgeAllAsync(DriverContext context,
        CancellationToken cancellationToken = default);

    /// <summary>Reports model-store disk usage.</summary>
    /// <param name="context">The driver context.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The disk usage.</returns>
    Task<CommandResponse<ModelDiskUsage>> DiskUsageAsync(DriverContext context,
        CancellationToken cancellationToken = default);
  }
}
