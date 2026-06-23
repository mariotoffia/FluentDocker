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
  /// CLI adapters (<c>docker model …</c>) and/or the native <c>/models*</c> socket
  /// adapter. Returns <see cref="CommandResponse{T}"/> — never throws for expected
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
    Task<CommandResponse<ModelInfo>> PullAsync(DriverContext context,
        ModelReference model, IProgress<ModelPullProgress> progress = null,
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
    Task<CommandResponse<ModelInfo>> InspectAsync(DriverContext context,
        ModelReference model, CancellationToken cancellationToken = default);

    /// <summary>Removes a local model.</summary>
    /// <param name="context">The driver context.</param>
    /// <param name="model">The model reference.</param>
    /// <param name="force">Whether to force removal.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A unit response.</returns>
    Task<CommandResponse<Unit>> RemoveAsync(DriverContext context,
        ModelReference model, bool force = false,
        CancellationToken cancellationToken = default);

    /// <summary>Tags a model under a new reference.</summary>
    /// <param name="context">The driver context.</param>
    /// <param name="source">The source reference.</param>
    /// <param name="target">The target reference.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A unit response.</returns>
    Task<CommandResponse<Unit>> TagAsync(DriverContext context,
        ModelReference source, ModelReference target,
        CancellationToken cancellationToken = default);

    /// <summary>Pushes a model to its registry.</summary>
    /// <param name="context">The driver context.</param>
    /// <param name="model">The model reference.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A unit response.</returns>
    Task<CommandResponse<Unit>> PushAsync(DriverContext context,
        ModelReference model, CancellationToken cancellationToken = default);

    /// <summary>Packages a GGUF file into an OCI model artifact.</summary>
    /// <param name="context">The driver context.</param>
    /// <param name="request">The package request.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The produced model's info.</returns>
    Task<CommandResponse<ModelInfo>> PackageAsync(DriverContext context,
        ModelPackageRequest request, CancellationToken cancellationToken = default);

    /// <summary>Prunes unused models.</summary>
    /// <param name="context">The driver context.</param>
    /// <param name="all">Whether to prune all (not just dangling).</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The prune result.</returns>
    Task<CommandResponse<ModelPruneResult>> PruneAsync(DriverContext context,
        bool all = false, CancellationToken cancellationToken = default);

    /// <summary>Reports model-store disk usage.</summary>
    /// <param name="context">The driver context.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The disk usage.</returns>
    Task<CommandResponse<ModelDiskUsage>> DiskUsageAsync(DriverContext context,
        CancellationToken cancellationToken = default);
  }
}
