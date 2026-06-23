using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Model.Models;
using FluentDocker.Model.Models.Options;

namespace FluentDocker.Services
{
  /// <summary>
  /// Model distribution and local-store operations: pull, list, inspect, remove,
  /// tag, push, package, prune and disk usage. The public façade over the
  /// management driver port (failures surface as <c>ModelRunnerException</c>).
  /// </summary>
  public interface IModelStore
  {
    /// <summary>Pulls a model, reporting progress.</summary>
    Task<ModelInfo> PullAsync(ModelReference model,
        IProgress<ModelPullProgress> progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>Lists local models.</summary>
    Task<IReadOnlyList<ModelInfo>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Inspects a local model.</summary>
    Task<ModelInfo> InspectAsync(ModelReference model, CancellationToken cancellationToken = default);

    /// <summary>Removes a local model.</summary>
    Task RemoveAsync(ModelReference model, bool force = false, CancellationToken cancellationToken = default);

    /// <summary>Tags a model under a new reference.</summary>
    Task TagAsync(ModelReference source, ModelReference target, CancellationToken cancellationToken = default);

    /// <summary>Pushes a model to its registry.</summary>
    Task PushAsync(ModelReference model, CancellationToken cancellationToken = default);

    /// <summary>Packages a GGUF file into an OCI model artifact.</summary>
    Task<ModelInfo> PackageAsync(ModelPackageRequest request, CancellationToken cancellationToken = default);

    /// <summary>Prunes unused models.</summary>
    Task<ModelPruneResult> PruneAsync(bool all = false, CancellationToken cancellationToken = default);

    /// <summary>Reports model-store disk usage.</summary>
    Task<ModelDiskUsage> DiskUsageAsync(CancellationToken cancellationToken = default);
  }
}
