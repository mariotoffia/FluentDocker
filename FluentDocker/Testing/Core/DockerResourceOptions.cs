using System;

namespace FluentDocker.Testing.Core
{
  /// <summary>
  /// Shared configuration for all Docker test resources.
  /// </summary>
  public class DockerResourceOptions
  {
    /// <summary>
    /// Driver to use for this resource. Defaults to <see cref="DriverSelection.Default"/>.
    /// </summary>
    public DriverSelection Driver { get; set; } = DriverSelection.Default;

    /// <summary>
    /// Whether to force-remove the resource on disposal even if stop fails.
    /// </summary>
    public bool ForceRemoveOnDispose { get; set; } = true;

    /// <summary>
    /// Timeout for initialization (including readiness waits).
    /// </summary>
    public TimeSpan InitializationTimeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Whether to capture logs on failure for diagnostics.
    /// </summary>
    public bool CaptureLogsOnFailure { get; set; } = true;

    /// <summary>
    /// Maximum log lines to capture on failure.
    /// </summary>
    public int MaxDiagnosticLogLines { get; set; } = 200;

    /// <summary>
    /// Timeout for teardown (stop + remove) during disposal.
    /// Prevents hung cleanup from blocking CI pipelines indefinitely.
    /// </summary>
    public TimeSpan TeardownTimeout { get; set; } = TimeSpan.FromSeconds(120);
  }
}
