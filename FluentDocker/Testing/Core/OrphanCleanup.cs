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
  /// <summary>
  /// Constants for the session-tracking label applied to all test resources.
  /// </summary>
  public static class SessionLabel
  {
    /// <summary>
    /// The label key applied to all resources created by the testing framework.
    /// </summary>
    public const string Key = "fluentdocker.session";

    /// <summary>
    /// The label key used to record when the resource was created (UTC ISO-8601).
    /// </summary>
    public const string CreatedAtKey = "fluentdocker.created-at";

    /// <summary>
    /// The label identifying the resource as managed by FluentDocker testing.
    /// </summary>
    public const string ManagedKey = "fluentdocker.managed";

    /// <summary>
    /// Generates a new unique session ID.
    /// </summary>
    public static string NewSessionId() => Guid.NewGuid().ToString("N");

    /// <summary>
    /// Builds the standard set of labels for a test resource.
    /// </summary>
    /// <param name="sessionId">The current session ID.</param>
    /// <returns>Dictionary of labels to apply.</returns>
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

  /// <summary>
  /// Utility for cleaning up orphaned test resources from previous sessions.
  /// Cleanup scans containers, networks, and volumes only. Compose-, topology-,
  /// swarm-stack-, and kubernetes-created resources are not cleaned unless the
  /// individual container, network, or volume carries the
  /// <see cref="SessionLabel.ManagedKey"/> label.
  /// </summary>
  public static class OrphanCleanup
  {
    private static readonly TimeSpan DefaultMinimumAge = TimeSpan.FromHours(1);
    private static readonly ConcurrentDictionary<string, byte> AbandonedLateProvisionNames = new();

    /// <summary>
    /// Result of an orphan cleanup operation that scans containers, networks,
    /// and volumes only.
    /// </summary>
    public class CleanupResult
    {
      /// <summary>Number of containers removed.</summary>
      public int ContainersRemoved { get; set; }

      /// <summary>Number of networks removed.</summary>
      public int NetworksRemoved { get; set; }

      /// <summary>Number of volumes removed.</summary>
      public int VolumesRemoved { get; set; }

      /// <summary>Errors encountered during cleanup (non-fatal).</summary>
      public List<string> Errors { get; set; } = [];

      /// <summary>Total resources removed.</summary>
      public int TotalRemoved => ContainersRemoved + NetworksRemoved + VolumesRemoved;
    }

    /// <summary>
    /// Removes FluentDocker-managed containers, networks, and volumes that do
    /// not belong to the specified current session. This cleans up resources
    /// orphaned by crashed or interrupted test runs. When
    /// <paramref name="minimumAge"/> is greater than zero, resources from other
    /// sessions are preserved until their <see cref="SessionLabel.CreatedAtKey"/>
    /// label is older than that age; missing or unparseable created-at labels
    /// are preserved fail-safe.
    /// </summary>
    /// <param name="kernel">The kernel with registered drivers.</param>
    /// <param name="driverId">The driver to clean up with.</param>
    /// <param name="currentSessionId">The current session ID to preserve (null to remove all).</param>
    /// <param name="minimumAge">Minimum age before another session's managed resource can be removed. Use overloads without this parameter for the default one-hour guard; pass <see cref="TimeSpan.Zero"/> only as an explicit opt-in.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Summary of removed resources.</returns>
    public static async Task<CleanupResult> CleanupOrphanedResourcesAsync(
        FluentDockerKernel kernel,
        string driverId,
        string currentSessionId = null,
        TimeSpan minimumAge = default,
        CancellationToken cancellationToken = default)
    {
      var result = new CleanupResult();
      var context = new DriverContext(driverId);

      // Clean up in dependency order: containers first, then networks, then volumes
      await CleanupContainersAsync(kernel, driverId, context, currentSessionId, minimumAge, result, cancellationToken).ConfigureAwait(false);
      await CleanupNetworksAsync(kernel, driverId, context, currentSessionId, minimumAge, result, cancellationToken).ConfigureAwait(false);
      await CleanupVolumesAsync(kernel, driverId, context, currentSessionId, minimumAge, result, cancellationToken).ConfigureAwait(false);

      return result;
    }

    /// <summary>
    /// Removes orphaned resources while preserving other sessions for the default one-hour safety window.
    /// </summary>
    public static Task<CleanupResult> CleanupOrphanedResourcesAsync(
        FluentDockerKernel kernel,
        string driverId)
    {
      return CleanupOrphanedResourcesAsync(
          kernel, driverId, null, DefaultMinimumAge, CancellationToken.None);
    }

    /// <summary>
    /// Removes orphaned resources while preserving other sessions for the default one-hour safety window.
    /// </summary>
    public static Task<CleanupResult> CleanupOrphanedResourcesAsync(
        FluentDockerKernel kernel,
        string driverId,
        string currentSessionId)
    {
      return CleanupOrphanedResourcesAsync(
          kernel, driverId, currentSessionId, DefaultMinimumAge, CancellationToken.None);
    }

    /// <summary>
    /// Removes FluentDocker-managed containers, networks, and volumes that do
    /// not belong to the specified current session, preserving resources from
    /// other sessions for the default one-hour safety window.
    /// </summary>
    /// <remarks>
    /// To disable the age guard intentionally, call the overload that accepts
    /// <c>minimumAge</c> and pass <see cref="TimeSpan.Zero"/>.
    /// </remarks>
    public static Task<CleanupResult> CleanupOrphanedResourcesAsync(
        FluentDockerKernel kernel,
        string driverId,
        string currentSessionId,
        CancellationToken cancellationToken)
    {
      return CleanupOrphanedResourcesAsync(
          kernel, driverId, currentSessionId, DefaultMinimumAge, cancellationToken);
    }

    private static async Task CleanupContainersAsync(
        FluentDockerKernel kernel, string driverId, DriverContext context,
        string currentSessionId, TimeSpan minimumAge, CleanupResult result,
        CancellationToken cancellationToken)
    {
      if (!kernel.TrySysCtl<IContainerDriver>(driverId, out var driver))
        return;

      var filter = new ContainerListFilter
      {
        All = true,
        Labels = { [SessionLabel.ManagedKey] = "true" }
      };

      var listResult = await driver.ListAsync(context, filter, cancellationToken).ConfigureAwait(false);
      if (!listResult.Success)
        return;

      foreach (var container in listResult.Data ?? Enumerable.Empty<Model.Containers.Container>())
      {
        var containerLabels = container.Config?.Labels as IDictionary<string, string>;
        if (containerLabels == null || containerLabels.Count == 0)
        {
          // ponytail: one inspect per managed CLI-listed container; upgrade path = map labels in list parsers.
          var inspect = await driver.InspectAsync(context, container.Id, cancellationToken).ConfigureAwait(false);
          if (inspect?.Success == true)
            containerLabels = inspect.Data?.Config?.Labels as IDictionary<string, string>;
        }
        var isAbandonedLateProvision = IsAbandonedLateProvision(container.Id, container.Name);
        if (IsCurrentSession(containerLabels, currentSessionId) && !isAbandonedLateProvision)
          continue;
        if (!isAbandonedLateProvision && ShouldPreserveDueToAge(containerLabels, minimumAge))
          continue;

        try
        {
          await driver.RemoveAsync(context, container.Id, force: true,
              removeVolumes: false, cancellationToken).ConfigureAwait(false);
          result.ContainersRemoved++;
        }
        catch (Exception ex)
        {
          result.Errors.Add($"Failed to remove container {container.Id}: {ex.Message}");
        }
      }
    }

    private static async Task CleanupNetworksAsync(
        FluentDockerKernel kernel, string driverId, DriverContext context,
        string currentSessionId, TimeSpan minimumAge, CleanupResult result,
        CancellationToken cancellationToken)
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
        var isAbandonedLateProvision = IsAbandonedLateProvision(network.Id, network.Name);
        if (IsCurrentSession(networkLabels, currentSessionId) && !isAbandonedLateProvision)
          continue;
        if (!isAbandonedLateProvision && ShouldPreserveDueToAge(networkLabels, minimumAge))
          continue;

        try
        {
          await driver.RemoveAsync(context, network.Id ?? network.Name, cancellationToken).ConfigureAwait(false);
          result.NetworksRemoved++;
        }
        catch (Exception ex)
        {
          result.Errors.Add($"Failed to remove network {network.Name}: {ex.Message}");
        }
      }
    }

    private static async Task CleanupVolumesAsync(
        FluentDockerKernel kernel, string driverId, DriverContext context,
        string currentSessionId, TimeSpan minimumAge, CleanupResult result,
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

      foreach (var volume in listResult.Data ?? Enumerable.Empty<Model.Volumes.Volume>())
      {
        var volumeLabels = volume.Labels as IDictionary<string, string>;
        var isAbandonedLateProvision = IsAbandonedLateProvision(volume.Name);
        if (IsCurrentSession(volumeLabels, currentSessionId) && !isAbandonedLateProvision)
          continue;
        if (!isAbandonedLateProvision && ShouldPreserveDueToAge(volumeLabels, minimumAge))
          continue;

        try
        {
          await driver.RemoveAsync(context, volume.Name, force: true, cancellationToken).ConfigureAwait(false);
          result.VolumesRemoved++;
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
        return false; // Remove all when no session specified

      if (labels == null)
        return false;

      return labels.TryGetValue(SessionLabel.Key, out var sessionId)
             && sessionId == currentSessionId;
    }

    internal static void MarkAbandonedLateProvision(string resourceName)
    {
      if (!string.IsNullOrWhiteSpace(resourceName))
        AbandonedLateProvisionNames.TryAdd(NormalizeResourceName(resourceName), 0);
    }

    private static bool IsAbandonedLateProvision(params string[] names)
    {
      foreach (var name in names)
      {
        if (!string.IsNullOrWhiteSpace(name) &&
            AbandonedLateProvisionNames.TryRemove(NormalizeResourceName(name), out _))
          return true;
      }

      return false;
    }

    private static string NormalizeResourceName(string name) =>
        name.Trim().TrimStart('/');

    private static bool ShouldPreserveDueToAge(
        IDictionary<string, string> labels,
        TimeSpan minimumAge)
    {
      if (minimumAge <= TimeSpan.Zero)
        return false;

      if (labels == null ||
          !labels.TryGetValue(SessionLabel.CreatedAtKey, out var createdAt) ||
          !DateTime.TryParse(
              createdAt,
              CultureInfo.InvariantCulture,
              DateTimeStyles.RoundtripKind,
              out var created))
        return true;

      return created.ToUniversalTime() > DateTime.UtcNow - minimumAge;
    }
  }
}
