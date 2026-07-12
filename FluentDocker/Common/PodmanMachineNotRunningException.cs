#nullable enable
using System;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Common
{
  /// <summary>
  /// Exception thrown when a Podman machine is required but not running.
  /// On macOS and Windows, Podman requires a Linux VM to be running
  /// for container operations. Use <c>podman machine start</c> to start
  /// the default machine, or configure <c>WithAutoStartMachine()</c> on
  /// the driver builder to handle this automatically.
  /// </summary>
  public class PodmanMachineNotRunningException : DriverException
  {
    /// <summary>
    /// Creates a new instance with the specified message.
    /// </summary>
    public PodmanMachineNotRunningException(string message)
        : base(message, ErrorCodes.Machine.NotRunning, true) { }

    /// <summary>
    /// Creates a new instance with the specified message and inner exception.
    /// </summary>
    public PodmanMachineNotRunningException(string message, Exception? innerException)
        : base(message, ErrorCodes.Machine.NotRunning, null, innerException, true) { }

    /// <summary>
    /// Creates a new instance with the specified message and explicit transiency. Use
    /// <c>isTransient: false</c> for cases that reuse this exception's error code for a
    /// permanent configuration error rather than a "machine stopped, retry" condition
    /// (e.g. an ambiguous-machine auto-start failure).
    /// </summary>
    public PodmanMachineNotRunningException(string message, bool isTransient)
        : base(message, ErrorCodes.Machine.NotRunning, isTransient) { }
  }
}
