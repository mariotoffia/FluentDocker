using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Testing.Core
{
  /// <summary>
  /// Sweeps up Docker/Podman containers, networks, and volumes that FluentDocker test resources
  /// created (tagged via <see cref="SessionLabel"/>) but never got to remove themselves — typically
  /// because a prior test process crashed, was killed, or was debugged past its normal lifetime. Only
  /// resources labeled <see cref="SessionLabel.ManagedKey"/> are ever touched, and a resource still in
  /// use (e.g. a network with attached containers, or a volume still mounted) is skipped even if it
  /// otherwise matches. See <see cref="ProcessExitReaper"/> for automatic cleanup on process exit.
  /// </summary>
  public static partial class OrphanCleanup
  {
    private static readonly TimeSpan DefaultMinimumAge = TimeSpan.FromHours(1);
    private static readonly ConcurrentDictionary<string, byte> AbandonedLateProvisionNames = new();

    /// <summary>The outcome of an orphan-cleanup sweep.</summary>
    public class CleanupResult
    {
      /// <summary>Number of containers removed.</summary>
      public int ContainersRemoved { get; set; }
      /// <summary>Number of networks removed.</summary>
      public int NetworksRemoved { get; set; }
      /// <summary>Number of volumes removed.</summary>
      public int VolumesRemoved { get; set; }
      /// <summary>
      /// Human-readable messages for resources that failed to remove. A failure here does not stop the
      /// sweep; other matching resources are still attempted.
      /// </summary>
      public List<string> Errors { get; set; } = [];
      /// <summary>The combined count of containers, networks, and volumes removed.</summary>
      public int TotalRemoved => ContainersRemoved + NetworksRemoved + VolumesRemoved;
    }

    /// <summary>
    /// Removes managed Docker/Podman resources belonging to sessions other than
    /// <paramref name="currentSessionId"/> that are at least <paramref name="minimumAge"/> old.
    /// </summary>
    /// <param name="kernel">The kernel to resolve driver ports from.</param>
    /// <param name="driverId">The driver id (pack) to sweep resources for.</param>
    /// <param name="currentSessionId">
    /// The calling session's id; its own resources are never removed. When <c>null</c>, only the
    /// shared session id from <see cref="SessionLabel.SessionEnvironmentVariable"/> (if any) is protected.
    /// </param>
    /// <param name="minimumAge">
    /// The minimum age a foreign-session resource must have before it is reclaimed; defaults to 1 hour
    /// when <c>null</c>. A resource whose age cannot be determined is preserved rather than removed.
    /// </param>
    /// <param name="cancellationToken">Token used to cancel the sweep.</param>
    /// <returns>A <see cref="CleanupResult"/> summarizing what was removed and any failures.</returns>
    public static Task<CleanupResult> CleanupOrphanedResourcesAsync(
        FluentDockerKernel kernel,
        string driverId,
        string? currentSessionId = null,
        TimeSpan? minimumAge = null,
        CancellationToken cancellationToken = default)
    {
      return CleanupResourcesAsync(
          kernel, driverId, currentSessionId, minimumAge ?? DefaultMinimumAge,
          targetSessionId: null, cancellationToken);
    }

    /// <summary>
    /// Overload of <see cref="CleanupOrphanedResourcesAsync(FluentDockerKernel, string, string, TimeSpan?, CancellationToken)"/>
    /// with no optional parameters.
    /// </summary>
    /// <param name="kernel">The kernel to resolve driver ports from.</param>
    /// <param name="driverId">The driver id (pack) to sweep resources for.</param>
    /// <param name="currentSessionId">The calling session's id; its own resources are never removed.</param>
    /// <param name="minimumAge">The minimum age a foreign-session resource must have before it is reclaimed.</param>
    /// <param name="cancellationToken">Token used to cancel the sweep.</param>
    /// <returns>A <see cref="CleanupResult"/> summarizing what was removed and any failures.</returns>
    public static Task<CleanupResult> CleanupOrphanedResourcesAsync(
        FluentDockerKernel kernel,
        string driverId,
        string? currentSessionId,
        TimeSpan minimumAge,
        CancellationToken cancellationToken)
    {
      return CleanupResourcesAsync(
          kernel, driverId, currentSessionId, minimumAge,
          targetSessionId: null, cancellationToken);
    }

    /// <summary>
    /// Removes every OTHER session's managed resources, keyed to one specific <paramref name="sessionId"/> —
    /// used by <see cref="ProcessExitReaper"/> to reap exactly its own session's resources on exit,
    /// bypassing the age/current-session filtering that
    /// <see cref="CleanupOrphanedResourcesAsync(FluentDockerKernel, string, string, TimeSpan?, CancellationToken)"/> applies.
    /// </summary>
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
        string? currentSessionId,
        TimeSpan minimumAge,
        string? targetSessionId,
        CancellationToken cancellationToken)
    {
      var result = new CleanupResult();
      var context = new DriverContext(driverId);
      var containers = await CleanupContainersAsync(kernel, driverId, context, currentSessionId, minimumAge, targetSessionId, result, cancellationToken).ConfigureAwait(false);
      await CleanupNetworksAsync(kernel, driverId, context, currentSessionId, minimumAge, targetSessionId, result, cancellationToken).ConfigureAwait(false);
      await CleanupVolumesAsync(kernel, driverId, context, currentSessionId, minimumAge, targetSessionId, containers, result, cancellationToken).ConfigureAwait(false);
      return result;
    }

    /// <summary>
    /// Overload of <see cref="CleanupOrphanedResourcesAsync(FluentDockerKernel, string, string, TimeSpan?, CancellationToken)"/>
    /// using the default minimum age, no session filter, and no cancellation.
    /// </summary>
    /// <param name="kernel">The kernel to resolve driver ports from.</param>
    /// <param name="driverId">The driver id (pack) to sweep resources for.</param>
    /// <returns>A <see cref="CleanupResult"/> summarizing what was removed and any failures.</returns>
    public static Task<CleanupResult> CleanupOrphanedResourcesAsync(
        FluentDockerKernel kernel,
        string driverId)
    {
      return CleanupOrphanedResourcesAsync(
          kernel, driverId, null, DefaultMinimumAge, CancellationToken.None);
    }

    /// <summary>
    /// Overload of <see cref="CleanupOrphanedResourcesAsync(FluentDockerKernel, string, string, TimeSpan?, CancellationToken)"/>
    /// using the default minimum age and no cancellation.
    /// </summary>
    /// <param name="kernel">The kernel to resolve driver ports from.</param>
    /// <param name="driverId">The driver id (pack) to sweep resources for.</param>
    /// <param name="currentSessionId">The calling session's id; its own resources are never removed.</param>
    /// <returns>A <see cref="CleanupResult"/> summarizing what was removed and any failures.</returns>
    public static Task<CleanupResult> CleanupOrphanedResourcesAsync(
        FluentDockerKernel kernel,
        string driverId,
        string? currentSessionId)
    {
      return CleanupOrphanedResourcesAsync(
          kernel, driverId, currentSessionId, DefaultMinimumAge, CancellationToken.None);
    }

    /// <summary>
    /// Overload of <see cref="CleanupOrphanedResourcesAsync(FluentDockerKernel, string, string, TimeSpan?, CancellationToken)"/>
    /// using the default minimum age.
    /// </summary>
    /// <param name="kernel">The kernel to resolve driver ports from.</param>
    /// <param name="driverId">The driver id (pack) to sweep resources for.</param>
    /// <param name="currentSessionId">The calling session's id; its own resources are never removed.</param>
    /// <param name="cancellationToken">Token used to cancel the sweep.</param>
    /// <returns>A <see cref="CleanupResult"/> summarizing what was removed and any failures.</returns>
    public static Task<CleanupResult> CleanupOrphanedResourcesAsync(
        FluentDockerKernel kernel,
        string driverId,
        string? currentSessionId,
        CancellationToken cancellationToken)
    {
      return CleanupOrphanedResourcesAsync(
          kernel, driverId, currentSessionId, DefaultMinimumAge, cancellationToken);
    }
  }
}
