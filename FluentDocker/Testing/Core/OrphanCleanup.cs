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
  /// Resources are identified by the <see cref="SessionLabel.Key"/> label.
  /// </summary>
  public static class OrphanCleanup
  {
    /// <summary>
    /// Result of an orphan cleanup operation.
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
    /// Removes all FluentDocker-managed resources that do NOT belong to the
    /// specified current session. This cleans up resources orphaned by crashed
    /// or interrupted test runs.
    /// </summary>
    /// <param name="kernel">The kernel with registered drivers.</param>
    /// <param name="driverId">The driver to clean up with.</param>
    /// <param name="currentSessionId">The current session ID to preserve (null to remove all).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Summary of removed resources.</returns>
    public static async Task<CleanupResult> CleanupOrphanedResourcesAsync(
        FluentDockerKernel kernel,
        string driverId,
        string currentSessionId = null,
        CancellationToken cancellationToken = default)
    {
      var result = new CleanupResult();
      var context = new DriverContext(driverId);

      // Clean up in dependency order: containers first, then networks, then volumes
      await CleanupContainersAsync(kernel, driverId, context, currentSessionId, result, cancellationToken).ConfigureAwait(false);
      await CleanupNetworksAsync(kernel, driverId, context, currentSessionId, result, cancellationToken).ConfigureAwait(false);
      await CleanupVolumesAsync(kernel, driverId, context, currentSessionId, result, cancellationToken).ConfigureAwait(false);

      return result;
    }

    private static async Task CleanupContainersAsync(
        FluentDockerKernel kernel, string driverId, DriverContext context,
        string currentSessionId, CleanupResult result,
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
        if (IsCurrentSession(containerLabels, currentSessionId))
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
        string currentSessionId, CleanupResult result,
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
        if (IsCurrentSession(network.Labels as IDictionary<string, string>, currentSessionId))
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
        string currentSessionId, CleanupResult result,
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
        if (IsCurrentSession(volume.Labels as IDictionary<string, string>, currentSessionId))
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
  }
}
