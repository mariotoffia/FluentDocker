using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Testing.Core
{
  /// <summary>
  /// OrphanCleanup partial: per-resource-kind sweep logic (containers, networks, volumes).
  /// Split out purely to keep each source file within the repository's 500-line limit.
  /// </summary>
  public static partial class OrphanCleanup
  {
    private static async Task<IList<Model.Containers.Container>> CleanupContainersAsync(
        FluentDockerKernel kernel, string driverId, DriverContext context,
        string currentSessionId, TimeSpan minimumAge, string targetSessionId,
        CleanupResult result, CancellationToken cancellationToken)
    {
      if (!kernel.TrySysCtl<IContainerDriver>(driverId, out var driver))
        return null;
      var filter = new ContainerListFilter
      {
        All = true,
        Labels = { [SessionLabel.ManagedKey] = "true" }
      };
      var listResult = await driver.ListAsync(context, filter, cancellationToken).ConfigureAwait(false);
      if (!listResult.Success)
        return null;
      var containers = listResult.Data ?? [];
      foreach (var container in containers)
      {
        var containerLabels = container.Config?.Labels as IDictionary<string, string>;
        Model.Containers.Container inspected = null;
        if (containerLabels == null || containerLabels.Count == 0)
        {
          // ponytail: one inspect per managed CLI-listed container; upgrade path = map labels in list parsers.
          inspected = await TryInspectContainerAsync(driver, context, container.Id, cancellationToken).ConfigureAwait(false);
          containerLabels = inspected?.Config?.Labels as IDictionary<string, string>;
        }
        var targetCleanup = !string.IsNullOrEmpty(targetSessionId);
        var isTargetSession = IsSession(containerLabels, targetSessionId);
        if (targetCleanup && !isTargetSession)
          continue;
        var isAbandonedLateProvision = !targetCleanup &&
            IsAbandonedLateProvision(containerLabels, container.Id, container.Name);
        if (!targetCleanup &&
            IsCurrentSession(containerLabels, currentSessionId) &&
            !isAbandonedLateProvision)
          continue;
        if (!targetCleanup && !isAbandonedLateProvision)
        {
          inspected ??= await TryInspectContainerAsync(driver, context, container.Id, cancellationToken).ConfigureAwait(false);
          var created = GetCreated(container, inspected);
          if (IsRunning(container, inspected))
          {
            // Running foreign-session containers are preserved unless the opt-in FLUENTDOCKER_REAP_RUNNING_AFTER ceiling marks them old enough to reclaim.
            if (!ShouldReapRunning(containerLabels, created))
              continue;
          }
          else if (ShouldPreserveDueToAge(containerLabels, minimumAge, created))
          {
            continue;
          }
        }
        try
        {
          var removed = await driver.RemoveAsync(context, container.Id, force: true,
              removeVolumes: false, cancellationToken).ConfigureAwait(false);
          if (!RemoveSucceeded(removed, "container", container.Id, result))
            continue;
          result.ContainersRemoved++;
          container.Mounts = [];
          if (isAbandonedLateProvision)
            ClearAbandonedLateProvision(containerLabels, container.Id, container.Name);
        }
        catch (Exception ex)
        {
          result.Errors.Add($"Failed to remove container {container.Id}: {ex.Message}");
        }
      }
      return containers;
    }

    private static async Task CleanupNetworksAsync(
        FluentDockerKernel kernel, string driverId, DriverContext context,
        string currentSessionId, TimeSpan minimumAge, string targetSessionId,
        CleanupResult result, CancellationToken cancellationToken)
    {
      if (!kernel.TrySysCtl<INetworkDriver>(driverId, out var driver))
        return;
      var filter = new NetworkListFilter
      {
        Labels = { [SessionLabel.ManagedKey] = "true" }
      };

      var listResult = await driver.ListAsync(context, filter, cancellationToken).ConfigureAwait(false);
      if (!listResult.Success)
        return;

      foreach (var network in listResult.Data ?? Enumerable.Empty<Network>())
      {
        var networkLabels = network.Labels as IDictionary<string, string>;
        var targetCleanup = !string.IsNullOrEmpty(targetSessionId);
        var isTargetSession = IsSession(networkLabels, targetSessionId);
        if (targetCleanup && !isTargetSession)
          continue;

        var isAbandonedLateProvision = !targetCleanup &&
            IsAbandonedLateProvision(networkLabels, network.Id, network.Name);

        if (!targetCleanup &&
            IsCurrentSession(networkLabels, currentSessionId) &&
            !isAbandonedLateProvision)
          continue;
        if (!targetCleanup &&
            !isAbandonedLateProvision &&
            ShouldPreserveDueToAge(networkLabels, minimumAge))
          continue;
        // ponytail: keep default-on orphan cleanup; in-use guard is the shared-daemon safety net.
        if (await IsNetworkInUseAsync(driver, context, network, cancellationToken)
            .ConfigureAwait(false))
          continue;

        try
        {
          var removed = await driver.RemoveAsync(context, network.Id ?? network.Name, cancellationToken).ConfigureAwait(false);
          if (!RemoveSucceeded(removed, "network", network.Name, result))
            continue;
          result.NetworksRemoved++;
          if (isAbandonedLateProvision)
            ClearAbandonedLateProvision(networkLabels, network.Id, network.Name);
        }
        catch (Exception ex)
        {
          result.Errors.Add($"Failed to remove network {network.Name}: {ex.Message}");
        }
      }
    }

    private static async Task CleanupVolumesAsync(
        FluentDockerKernel kernel, string driverId, DriverContext context,
        string currentSessionId, TimeSpan minimumAge, string targetSessionId,
        IList<Model.Containers.Container> containers, CleanupResult result,
        CancellationToken cancellationToken)
    {
      if (!kernel.TrySysCtl<IVolumeDriver>(driverId, out var driver))
        return;

      var filter = new VolumeListFilter
      {
        Labels = { [SessionLabel.ManagedKey] = "true" }
      };

      var listResult = await driver.ListAsync(context, filter, cancellationToken).ConfigureAwait(false);
      if (!listResult.Success)
        return;
      var inUseVolumes = GetInUseVolumes(containers);

      foreach (var volume in listResult.Data ?? Enumerable.Empty<Model.Volumes.Volume>())
      {
        var volumeLabels = volume.Labels as IDictionary<string, string>;
        var targetCleanup = !string.IsNullOrEmpty(targetSessionId);
        var isTargetSession = IsSession(volumeLabels, targetSessionId);
        if (targetCleanup && !isTargetSession)
          continue;

        var isAbandonedLateProvision = !targetCleanup &&
            IsAbandonedLateProvision(volumeLabels, volume.Name);

        if (!targetCleanup &&
            IsCurrentSession(volumeLabels, currentSessionId) &&
            !isAbandonedLateProvision)
          continue;
        if (!targetCleanup &&
            !isAbandonedLateProvision &&
            ShouldPreserveDueToAge(volumeLabels, minimumAge, volume.Created))
          continue;
        if (string.IsNullOrWhiteSpace(volume.Name) ||
            inUseVolumes == null ||
            inUseVolumes.Contains(volume.Name))
          continue;

        try
        {
          var removed = await driver.RemoveAsync(context, volume.Name, force: true, cancellationToken).ConfigureAwait(false);
          if (!RemoveSucceeded(removed, "volume", volume.Name, result))
            continue;
          result.VolumesRemoved++;
          if (isAbandonedLateProvision)
            ClearAbandonedLateProvision(volumeLabels, volume.Name);
        }
        catch (Exception ex)
        {
          result.Errors.Add($"Failed to remove volume {volume.Name}: {ex.Message}");
        }
      }
    }

    private static async Task<Model.Containers.Container> TryInspectContainerAsync(
        IContainerDriver driver,
        DriverContext context,
        string containerId,
        CancellationToken cancellationToken)
    {
      try
      {
        var inspect = await driver.InspectAsync(context, containerId, cancellationToken).ConfigureAwait(false);
        return inspect?.Success == true ? inspect.Data : null;
      }
      catch
      {
        return null;
      }
    }

    private static async Task<bool> IsNetworkInUseAsync(
        INetworkDriver driver,
        DriverContext context,
        Network network,
        CancellationToken cancellationToken)
    {
      if (network?.Containers?.Count > 0)
        return true;

      var id = network?.Id ?? network?.Name;
      if (string.IsNullOrWhiteSpace(id))
        return false;

      try
      {
        var inspect = await driver.InspectAsync(context, id, cancellationToken).ConfigureAwait(false);
        return inspect.Success && inspect.Data?.Containers?.Count > 0;
      }
      catch
      {
        return false;
      }
    }

    private static HashSet<string> GetInUseVolumes(
        IList<Model.Containers.Container> containers)
    {
      if (containers == null)
        return null;

      var volumes = new HashSet<string>(StringComparer.Ordinal);
      foreach (var container in containers)
      {
        foreach (var mount in container.Mounts ?? Enumerable.Empty<Model.Containers.ContainerMount>())
        {
          if (!string.IsNullOrWhiteSpace(mount.Name))
            volumes.Add(mount.Name);
          if (!string.IsNullOrWhiteSpace(mount.Source))
            volumes.Add(mount.Source);
        }
      }
      return volumes;
    }

    private static bool IsRunning(
        Model.Containers.Container listed,
        Model.Containers.Container inspected)
    {
      return inspected?.State?.Running == true ||
             listed?.State?.Running == true;
    }

    private static DateTimeOffset GetCreated(
        Model.Containers.Container listed,
        Model.Containers.Container inspected)
    {
      if (inspected?.Created != default)
        return inspected.Created;
      return listed?.Created ?? default;
    }
  }
}
