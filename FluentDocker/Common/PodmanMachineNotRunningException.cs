using System;

namespace FluentDocker.Common
{
  /// <summary>
  /// Exception thrown when a Podman machine is required but not running.
  /// On macOS and Windows, Podman requires a Linux VM to be running
  /// for container operations. Use <c>podman machine start</c> to start
  /// the default machine, or configure <c>WithAutoStartMachine()</c> on
  /// the driver builder to handle this automatically.
  /// </summary>
  public class PodmanMachineNotRunningException : Exception
  {
    /// <summary>
    /// Creates a new instance with the specified message.
    /// </summary>
    public PodmanMachineNotRunningException(string message)
        : base(message) { }

    /// <summary>
    /// Creates a new instance with the specified message and inner exception.
    /// </summary>
    public PodmanMachineNotRunningException(string message, Exception innerException)
        : base(message, innerException) { }
  }
}
