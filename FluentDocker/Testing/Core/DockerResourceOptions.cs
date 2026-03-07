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
    private TimeSpan _initializationTimeout = TimeSpan.FromMinutes(2);
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

    /// <summary>
    /// Maximum log lines to capture on failure.
    /// </summary>
    private int _maxDiagnosticLogLines = 200;
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
    /// Timeout for teardown (stop + remove) during disposal.
    /// Prevents hung cleanup from blocking CI pipelines indefinitely.
    /// </summary>
    private TimeSpan _teardownTimeout = TimeSpan.FromSeconds(120);
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
