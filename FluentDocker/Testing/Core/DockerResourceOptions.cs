using System;

namespace FluentDocker.Testing.Core
{
  /// <summary>
  /// Shared configuration for all Docker test resources.
  /// </summary>
  public class DockerResourceOptions
  {
    private static readonly string ProcessSessionId =
        CreateProcessSessionId();

    private static string CreateProcessSessionId()
    {
      var shared = Environment.GetEnvironmentVariable(SessionLabel.SessionEnvironmentVariable);
      return string.IsNullOrWhiteSpace(shared) ? SessionLabel.NewSessionId() : shared;
    }

    /// <summary>
    /// Driver to use for this resource. Defaults to <see cref="DriverSelection.Default"/>.
    /// </summary>
    public DriverSelection Driver { get; set; } = DriverSelection.Default;

    /// <summary>
    /// Whether to force-remove the resource on disposal even if stop fails.
    /// </summary>
    public bool ForceRemoveOnDispose { get; set; } = true;

    private TimeSpan _initializationTimeout = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Timeout for initialization (including readiness waits).
    /// </summary>
    public TimeSpan InitializationTimeout
    {
      get => _initializationTimeout;
      set
      {
        if (value <= TimeSpan.Zero)
          throw new ArgumentOutOfRangeException(
              nameof(value), value, "InitializationTimeout must be positive.");
        _initializationTimeout = value;
      }
    }

    /// <summary>
    /// Whether to capture logs on failure for diagnostics.
    /// </summary>
    public bool CaptureLogsOnFailure { get; set; } = true;

    private int _maxDiagnosticLogLines = 200;

    /// <summary>
    /// Maximum log lines to capture on failure.
    /// </summary>
    public int MaxDiagnosticLogLines
    {
      get => _maxDiagnosticLogLines;
      set
      {
        if (value < 0)
          throw new ArgumentOutOfRangeException(
              nameof(value), value, "MaxDiagnosticLogLines must be >= 0.");
        _maxDiagnosticLogLines = value;
      }
    }

    /// <summary>
    /// Session ID used for orphan tracking labels. Defaults to one ID per process
    /// or to <c>FLUENTDOCKER_TEST_SESSION</c> when set, so sibling test
    /// processes can share one live test session. Set this property to override
    /// the grouping.
    /// </summary>
    public string SessionId { get; set; } = ProcessSessionId;

    private TimeSpan _orphanCleanupMinimumAge = TimeSpan.FromHours(1);

    /// <summary>
    /// Minimum age a FluentDocker-managed resource from another session must reach
    /// before orphan cleanup may remove it. The default one-hour guard prevents
    /// <see cref="CleanupOrphansOnInit"/> from deleting live resources created by
    /// another test process in parallel CI. Set to <see cref="TimeSpan.Zero"/> to
    /// disable the age guard; negative values are rejected.
    /// </summary>
    public TimeSpan OrphanCleanupMinimumAge
    {
      get => _orphanCleanupMinimumAge;
      set
      {
        if (value < TimeSpan.Zero)
          throw new ArgumentOutOfRangeException(
              nameof(value), value, "OrphanCleanupMinimumAge must be >= 0.");
        _orphanCleanupMinimumAge = value;
      }
    }

    /// <summary>
    /// Whether to apply session-tracking labels to created resources.
    /// Honored directly by <see cref="ContainerResource"/>,
    /// <see cref="NetworkResource"/>, <see cref="VolumeResource"/>, and
    /// <see cref="TopologyResource"/> child container/network/volume operations.
    /// Compose injects an overlay when possible. Swarm stack and Podman Kubernetes
    /// resources cannot add labels automatically; use unique names and explicit
    /// cleanup for those resource families. Default: true.
    /// </summary>
    public bool EnableSessionLabels { get; set; } = true;

    /// <summary>
    /// Whether to clean up orphaned resources from previous sessions
    /// during <see cref="ResourceBase.InitializeAsync"/>. Default: true.
    /// The sweep runs once per (driver id, session id) per PROCESS — a deliberate
    /// de-duplication so per-test fixtures do not pay O(tests) sweeps. Consequence:
    /// two kernels registering the SAME driver id against DIFFERENT daemon endpoints
    /// share one sweep slot, and only the first endpoint is swept. Register a distinct
    /// driver id per endpoint when per-endpoint sweeps matter.
    /// Exit reaping is ON by default: on process exit, SIGINT (Ctrl-C), and SIGTERM the
    /// current session's own managed resources are force-removed as a best-effort sweep —
    /// <em>including still-running containers</em>. Set
    /// <c>FLUENTDOCKER_TEST_REAPER_ON_EXIT=0</c> (or <c>false</c>) to opt out, e.g. when you
    /// Ctrl-C a run specifically to inspect a container you want left alive.
    /// Shared <c>FLUENTDOCKER_TEST_SESSION</c> sessions skip exit reaping so one
    /// process cannot delete a sibling process's live fixtures.
    /// SIGKILL and hard host termination cannot be caught.
    /// </summary>
    public bool CleanupOrphansOnInit { get; set; } = true;

    private TimeSpan _teardownTimeout = TimeSpan.FromSeconds(120);

    /// <summary>
    /// Timeout for teardown (stop + remove) during disposal.
    /// Prevents hung cleanup from blocking CI pipelines indefinitely.
    /// </summary>
    public TimeSpan TeardownTimeout
    {
      get => _teardownTimeout;
      set
      {
        if (value <= TimeSpan.Zero)
          throw new ArgumentOutOfRangeException(
              nameof(value), value, "TeardownTimeout must be positive.");
        _teardownTimeout = value;
      }
    }
  }
}
