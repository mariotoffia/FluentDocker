#nullable enable
// ReSharper disable InconsistentNaming
namespace FluentDocker.Model.Containers
{
  /// <summary>
  /// A POSIX signal that can be sent to a container's main process (for example via <c>docker kill</c>).
  /// </summary>
  [System.Obsolete("Unused by FluentDocker and scheduled for removal in a future release. Pass signal names as strings instead.")]
  public enum UnixSignal
  {
    /// <summary>SIGHUP — hangup; commonly used to make a process reload its configuration.</summary>
    SIGHUP,

    /// <summary>SIGTERM — request a graceful termination.</summary>
    SIGTERM,

    /// <summary>SIGKILL — force immediate termination; cannot be caught or ignored.</summary>
    SIGKILL
  }
}
