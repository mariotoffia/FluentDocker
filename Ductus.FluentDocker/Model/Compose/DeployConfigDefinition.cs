namespace Ductus.FluentDocker.Model.Compose
{
  public sealed class DeployConfigDefinition
  {
    /// <summary>
    /// The number of container at a time for the operation.
    /// </summary>
    /// <remarks>
    /// Rollback:  The number of containers to rollback at a time. If set to 0, all containers rollback simultaneously.
    /// Update: The number of containers to update at a time.
    /// </remarks>
    public int Parallelism { get; set; } = 0;
    /// <summary>
    /// The time to wait.
    /// </summary>
    /// <remarks>
    /// Rollback: The time to wait between each container group’s rollback (default 0s).
    /// Update: The time to wait between updating a group of containers.
    /// </remarks>
    public string Delay { get; set; }
    /// <summary>
    /// What to do if fails (default pause).
    /// </summary>
    /// <remarks>
    /// Rollback: What to do if a rollback fails. One of continue or pause.
    /// Update: What to do if an update fails. One of continue, rollback, or pause.
    /// </remarks>
    public string FailureAction { get; set; } = "pause";
    /// <summary>
    /// Duration after each task update to monitor for failure.
    /// </summary>
    /// <remarks>
    /// (ns|us|ms|s|m|h) - default 0s.
    /// </remarks>
    public string Monitor { get; set; } = "0s";
    /// <summary>
    ///  Failure rate to tolerate during rollback or update, default 0.
    /// </summary>
    public int MaxFailureRatio { get; set; } = 0;
    /// <summary>
    /// Order of operations during updates or rollback.
    /// </summary>
    /// <remarks>
    /// Note: Only supported for v3.4 and higher.
    /// One of stop-first (old task is stopped before starting new one), or start-first (new task is started first,
    /// and the running tasks briefly overlap) (default stop-first).
    /// </remarks>
    public string Order { get; set; } = "stop-first";
  }
}
