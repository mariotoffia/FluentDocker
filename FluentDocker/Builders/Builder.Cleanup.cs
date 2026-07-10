using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Kernel;
using FluentDocker.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FluentDocker.Builders
{
  public partial class Builder
  {
    private static async Task<BuildFailureManifest> CleanupFailedBuildAsync(
        IReadOnlyList<(BuildOperation Operation, IServiceAsync Service)> completedOperations,
        TimeSpan cleanupTimeout)
    {
      var removed = new List<BuildFailureResource>();
      var kept = new List<BuildFailureResource>();

      // Shared deadline across the whole sweep: cleanupTimeout is a total bound (see
      // IBuilder docs), not a per-resource budget. A wedged daemon can no longer stretch
      // "bounded" cleanup to timeout × resourceCount.
      using var cleanupCts = new CancellationTokenSource(cleanupTimeout);

      // Reverse creation order: dependents before dependencies on failure too.
      for (var i = completedOperations.Count - 1; i >= 0; i--)
      {
        var (operation, service) = completedOperations[i];
        try
        {
          if (operation.ForceRemoveOnFailure?.Invoke(service) == true)
          {
            if (service.State == ServiceRunningState.Removed)
            {
              removed.Add(ToResource(service, "builder-created"));
              continue;
            }

            // Cleanup ownership: builder-created resources are removed on failure; borrowed ones are untouched.
            await service.RemoveAsync(force: true, cleanupCts.Token).ConfigureAwait(false);
            removed.Add(ToResource(service, "builder-created"));
            continue;
          }

          await DisposeServiceAsync(service, cleanupCts.Token).ConfigureAwait(false);
          var reason = operation.FailureKeepReason?.Invoke(service);
          if (!string.IsNullOrEmpty(reason))
            kept.Add(ToResource(service, reason));
          else if (service.State == ServiceRunningState.Removed)
            removed.Add(ToResource(service, "disposed"));
          else
            // Disposed but not removed (e.g. Stopped): still on the daemon — the
            // resource most likely to linger must appear in the manifest, not vanish.
            kept.Add(ToResource(service, "disposed but not removed"));
        }
        catch (Exception ex)
        {
          var resource = ToResource(service, $"cleanup failed: {ex.Message}", ex);
          LogCleanupFailure(service, resource, ex);
          kept.Add(resource);
        }
      }

      return new BuildFailureManifest(removed, kept);
    }

    private static async Task DisposeServiceAsync(
        IServiceAsync service, CancellationToken cancellationToken)
    {
      var task = service is IAsyncDisposable asyncDisposable
          ? asyncDisposable.DisposeAsync().AsTask()
          : Task.Run(() => service.Dispose(), CancellationToken.None);
      await task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static BuildFailureResource ToResource(
        IServiceAsync service, string reason, Exception? exception = null)
    {
      var kind = service switch
      {
        IContainerService => "container",
        INetworkService => "network",
        IVolumeService => "volume",
        IComposeService => "compose",
        IPodService => "pod",
        IImageService => "image",
        _ => service.GetType().Name
      };
      var id = service switch
      {
        IContainerService container => container.Id,
        INetworkService network => network.Id,
        IVolumeService volume => volume.VolumeName,
        IPodService pod => pod.Id,
        _ => service.Name
      };

      return new BuildFailureResource(kind, service.Name, id, reason, exception);
    }

    private static void LogCleanupFailure(
        IServiceAsync service, BuildFailureResource resource, Exception exception)
    {
      var logger = (service.Kernel?.LoggerFactory ?? NullLoggerFactory.Instance)
          .CreateLogger<Builder>();
      logger.LogWarning(
          exception,
          "Build failure cleanup failed for {ResourceKind} {ResourceName}",
          resource.Kind,
          resource.Name ?? resource.Id ?? "<unnamed>");
    }
  }
}
