namespace Ductus.FluentDocker.Model.Containers
{
  public enum RestartPolicy
  {
    /// <summary>
    /// Do not automatically restart the container. (the default).
    /// </summary>
    No = 0,
    /// <summary>
    /// Restart the container if it exits due to an error, which manifests as a non-zero exit code.
    /// </summary>
    OnFailure = 1,
    /// <summary>    
    /// Restart the container unless it is explicitly stopped or Docker itself is stopped or restarted.
    /// </summary>
    UnlessStopped = 2,
    /// <summary>
    /// Always restart the container if it stops.
    /// </summary>
    Always = 3
  }
}
