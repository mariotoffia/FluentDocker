using System;
using System.Collections.Generic;

namespace FluentDocker.Testing.Core
{
  /// <summary>
  /// Docker/Podman label keys and environment variable names FluentDocker's test resources
  /// (<see cref="ResourceBase"/> derivatives) use to tag the containers, networks, and volumes they
  /// create, so <see cref="OrphanCleanup"/> and <see cref="ProcessExitReaper"/> can later identify and
  /// reclaim resources belonging to a finished or abandoned test session.
  /// </summary>
  public static class SessionLabel
  {
    /// <summary>Label key holding the session id a resource belongs to.</summary>
    public const string Key = "fluentdocker.session";

    /// <summary>Label key holding the resource's creation timestamp (round-trip <c>"o"</c> format), set by FluentDocker.</summary>
    public const string CreatedAtKey = "fluentdocker.created-at";

    /// <summary>
    /// Label key marking a resource as created and owned by FluentDocker test infrastructure; only
    /// resources carrying this label (value <c>"true"</c>) are candidates for orphan cleanup.
    /// </summary>
    public const string ManagedKey = "fluentdocker.managed";

    /// <summary>
    /// Environment variable naming a session id shared across multiple test processes, so they treat
    /// each other's resources as belonging to the current session instead of as orphans.
    /// </summary>
    public const string SessionEnvironmentVariable = "FLUENTDOCKER_TEST_SESSION";

    /// <summary>
    /// Environment variable controlling best-effort cleanup of the current session's resources on
    /// process exit, SIGINT, and SIGTERM (see <see cref="ProcessExitReaper"/>). Exit reaping is
    /// <b>ON by default</b> (it force-removes even running session containers on Ctrl-C); set this
    /// variable to <c>0</c>/<c>false</c> to opt out.
    /// </summary>
    public const string ReaperEnvironmentVariable = "FLUENTDOCKER_TEST_REAPER_ON_EXIT";

    /// <summary>
    /// Environment variable setting an age ceiling (e.g. <c>"30m"</c>, <c>"2h"</c>, <c>"1d"</c>) past
    /// which a still-<em>running</em> foreign-session container is reclaimed by orphan cleanup even
    /// though it is running; unset means running containers from other sessions are never reaped.
    /// </summary>
    public const string ReapRunningAfterEnvironmentVariable = "FLUENTDOCKER_REAP_RUNNING_AFTER";

    /// <summary>Generates a new random session id.</summary>
    /// <returns>A 32-character hex string (a <see cref="Guid"/> in <c>"N"</c> format).</returns>
    public static string NewSessionId() => Guid.NewGuid().ToString("N");

    internal static string? SharedSessionId() =>
        Environment.GetEnvironmentVariable(SessionEnvironmentVariable);

    /// <summary>Builds the label set FluentDocker stamps onto a newly created resource.</summary>
    /// <param name="sessionId">The session id to tag the resource with (see <see cref="Key"/>).</param>
    /// <returns>Labels for <see cref="Key"/> (<paramref name="sessionId"/>), <see cref="CreatedAtKey"/> (now, UTC), and <see cref="ManagedKey"/> ("true").</returns>
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
}
