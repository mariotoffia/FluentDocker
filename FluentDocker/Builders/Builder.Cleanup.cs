using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Model.Kernel;
using FluentDocker.Services;

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
      using var cleanupCts = new CancellationTokenSource(cleanupTimeout);

      // Reverse creation order: dependents before dependencies on failure too.
      for (var i = completedOperations.Count - 1; i >= 0; i--)
      {
        var (operation, service) = completedOperations[i];
        try
        {
          if (operation.ForceRemoveOnFailure?.Invoke(service) == true)
          {
            // Cleanup ownership: builder-created resources are removed on failure; borrowed ones are untouched.
            await service.RemoveAsync(force: true, cleanupCts.Token).ConfigureAwait(false);
            removed.Add(ToResource(service, "builder-created"));
            continue;
          }

          await DisposeServiceAsync(service, cleanupCts.Token).ConfigureAwait(false);
          var reason = operation.FailureKeepReason?.Invoke(service);
          if (!string.IsNullOrEmpty(reason))
            kept.Add(ToResource(service, reason));
        }
        catch (Exception ex)
        {
          kept.Add(ToResource(service, $"cleanup failed: {ex.Message}"));
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

    private static BuildFailureResource ToResource(IServiceAsync service, string reason)
    {
      var kind = service switch
      {
        IContainerService => "container",
        INetworkService => "network",
        IVolumeService => "volume",
        IComposeService => "compose",
        IImageService => "image",
        _ => service.GetType().Name
      };
      var id = service switch
      {
        IContainerService container => container.Id,
        INetworkService network => network.Id,
        IVolumeService volume => volume.VolumeName,
        _ => service.Name
      };

      return new BuildFailureResource(kind, service.Name, id, reason);
    }
  }
}
