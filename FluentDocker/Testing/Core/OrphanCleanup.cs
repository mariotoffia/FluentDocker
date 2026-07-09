using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
namespace FluentDocker.Testing.Core
{
  public static class SessionLabel
  {
    public const string Key = "fluentdocker.session";
    public const string CreatedAtKey = "fluentdocker.created-at";
    public const string ManagedKey = "fluentdocker.managed";
    public const string SessionEnvironmentVariable = "FLUENTDOCKER_TEST_SESSION";
    public const string ReaperEnvironmentVariable = "FLUENTDOCKER_TEST_REAPER_ON_EXIT";
    public static string NewSessionId() => Guid.NewGuid().ToString("N");
    internal static string SharedSessionId() =>
        Environment.GetEnvironmentVariable(SessionEnvironmentVariable);
    public static Dictionary<string, string> CreateLabels(string sessionId)
    {
      return new Dictionary<string, string>
      {
        [Key] = sessionId,
        [CreatedAtKey] = DateTime.UtcNow.ToString("o"),
        [ManagedKey] = "true"
      };
    }
  }
  public static class OrphanCleanup
  {
    private static readonly TimeSpan DefaultMinimumAge = TimeSpan.FromHours(1);
    private static readonly ConcurrentDictionary<string, byte> AbandonedLateProvisionNames = new();
    public class CleanupResult
    {
      public int ContainersRemoved { get; set; }
      public int NetworksRemoved { get; set; }
      public int VolumesRemoved { get; set; }
      public List<string> Errors { get; set; } = [];
      public int TotalRemoved => ContainersRemoved + NetworksRemoved + VolumesRemoved;
    }
    public static Task<CleanupResult> CleanupOrphanedResourcesAsync(
        FluentDockerKernel kernel,
        string driverId,
        string currentSessionId = null,
        TimeSpan? minimumAge = null,
        CancellationToken cancellationToken = default)
    {
      return CleanupResourcesAsync(
          kernel, driverId, currentSessionId, minimumAge ?? DefaultMinimumAge,
          targetSessionId: null, cancellationToken);
    }
    public static Task<CleanupResult> CleanupOrphanedResourcesAsync(
        FluentDockerKernel kernel,
        string driverId,
        string currentSessionId,
        TimeSpan minimumAge,
        CancellationToken cancellationToken)
    {
      return CleanupResourcesAsync(
          kernel, driverId, currentSessionId, minimumAge,
          targetSessionId: null, cancellationToken);
    }
    internal static Task<CleanupResult> CleanupSessionResourcesAsync(
        FluentDockerKernel kernel,
        string driverId,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
      return CleanupResourcesAsync(
          kernel, driverId, currentSessionId: null, minimumAge: TimeSpan.Zero,
          targetSessionId: sessionId, cancellationToken);
    }
    private static async Task<CleanupResult> CleanupResourcesAsync(
        FluentDockerKernel kernel,
        string driverId,
        string currentSessionId,
        TimeSpan minimumAge,
        string targetSessionId,
        CancellationToken cancellationToken)
    {
      var result = new CleanupResult();
      var context = new DriverContext(driverId);
      var containers = await CleanupContainersAsync(kernel, driverId, context, currentSessionId, minimumAge, targetSessionId, result, cancellationToken).ConfigureAwait(false);
      await CleanupNetworksAsync(kernel, driverId, context, currentSessionId, minimumAge, targetSessionId, result, cancellationToken).ConfigureAwait(false);
      await CleanupVolumesAsync(kernel, driverId, context, currentSessionId, minimumAge, targetSessionId, containers, result, cancellationToken).ConfigureAwait(false);
      return result;
    }
    public static Task<CleanupResult> CleanupOrphanedResourcesAsync(
        FluentDockerKernel kernel,
        string driverId)
    {
      return CleanupOrphanedResourcesAsync(
          kernel, driverId, null, DefaultMinimumAge, CancellationToken.None);
    }
    public static Task<CleanupResult> CleanupOrphanedResourcesAsync(
        FluentDockerKernel kernel,
        string driverId,
        string currentSessionId)
    {
      return CleanupOrphanedResourcesAsync(
          kernel, driverId, currentSessionId, DefaultMinimumAge, CancellationToken.None);
    }
    public static Task<CleanupResult> CleanupOrphanedResourcesAsync(
        FluentDockerKernel kernel,
        string driverId,
        string currentSessionId,
        CancellationToken cancellationToken)
    {
      return CleanupOrphanedResourcesAsync(
          kernel, driverId, currentSessionId, DefaultMinimumAge, cancellationToken);
    }
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
          if (IsRunning(container, inspected))
            continue;
          if (ShouldPreserveDueToAge(containerLabels, minimumAge, GetCreated(container, inspected)))
            continue;
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

    private static bool IsCurrentSession(
        IDictionary<string, string> labels, string currentSessionId)
    {
      if (currentSessionId == null)
        return IsSession(labels, SessionLabel.SharedSessionId());

      return IsSession(labels, currentSessionId) ||
             IsSession(labels, SessionLabel.SharedSessionId());
    }

    private static bool IsSession(IDictionary<string, string> labels, string sessionId)
    {
      if (labels == null || string.IsNullOrWhiteSpace(sessionId))
        return false;
      return labels.TryGetValue(SessionLabel.Key, out var resourceSessionId)
             && resourceSessionId == sessionId;
    }

    internal static void MarkAbandonedLateProvision(string resourceName, string sessionId)
    {
      if (!string.IsNullOrWhiteSpace(resourceName) &&
          !string.IsNullOrWhiteSpace(sessionId))
        AbandonedLateProvisionNames.TryAdd(
            AbandonedLateProvisionKey(resourceName, sessionId), 0);
    }

    private static bool IsAbandonedLateProvision(
        IDictionary<string, string> labels,
        params string[] names)
    {
      if (labels == null ||
          !labels.TryGetValue(SessionLabel.Key, out var sessionId) ||
          string.IsNullOrWhiteSpace(sessionId))
        return false;

      foreach (var name in names)
      {
        if (!string.IsNullOrWhiteSpace(name) &&
            AbandonedLateProvisionNames.ContainsKey(
                AbandonedLateProvisionKey(name, sessionId)))
          return true;
      }

      return false;
    }

    private static void ClearAbandonedLateProvision(
        IDictionary<string, string> labels,
        params string[] names)
    {
      if (labels == null ||
          !labels.TryGetValue(SessionLabel.Key, out var sessionId) ||
          string.IsNullOrWhiteSpace(sessionId))
        return;

      foreach (var name in names)
      {
        if (!string.IsNullOrWhiteSpace(name))
          AbandonedLateProvisionNames.TryRemove(
              AbandonedLateProvisionKey(name, sessionId), out _);
      }
    }

    private static string AbandonedLateProvisionKey(string name, string sessionId) =>
        $"{sessionId}\u001f{NormalizeResourceName(name)}";

    private static string NormalizeResourceName(string name) =>
        name.Trim().TrimStart('/');

    private static bool ShouldPreserveDueToAge(
        IDictionary<string, string> labels,
        TimeSpan minimumAge,
        DateTimeOffset daemonCreated = default)
    {
      if (minimumAge <= TimeSpan.Zero)
        return false;

      if (daemonCreated != default)
        return daemonCreated.ToUniversalTime() > DateTimeOffset.UtcNow - minimumAge;

      if (labels == null ||
          !labels.TryGetValue(SessionLabel.CreatedAtKey, out var createdAt) ||
          !DateTimeOffset.TryParse(
              createdAt,
              CultureInfo.InvariantCulture,
              DateTimeStyles.RoundtripKind,
              out var created))
        return true;

      return created.ToUniversalTime() > DateTimeOffset.UtcNow - minimumAge;
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

    private static bool RemoveSucceeded(
        CommandResponse<Unit> response,
        string type,
        string name,
        CleanupResult result)
    {
      if (response?.Success == true)
        return true;
      result.Errors.Add($"Failed to remove {type} {name}: {response?.Error ?? "unknown error"}");
      return false;
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
