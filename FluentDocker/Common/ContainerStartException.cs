using System;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Common
{
  /// <summary>
  /// Exception thrown when a container fails to start.
  /// </summary>
  public class ContainerStartException : DriverException
  {
    /// <summary>
    /// The identifier of the container that failed to start.
    /// </summary>
    public string ContainerId { get; }

    /// <summary>
    /// Initializes a new instance with the specified error message.
    /// </summary>
    /// <param name="message">The error message describing the start failure.</param>
    public ContainerStartException(string message)
        : base(message, ErrorCodes.Container.StartFailed, null, isTransient: true)
    {
    }

    /// <summary>
    /// Initializes a new instance with the specified container identifier, reason, and error context.
    /// </summary>
    /// <param name="containerId">The identifier of the container that failed to start.</param>
    /// <param name="reason">The reason the container failed to start.</param>
    /// <param name="context">Diagnostic context information.</param>
    public ContainerStartException(string containerId, string reason, ErrorContext context)
        : base($"Failed to start container '{containerId}': {reason}", ErrorCodes.Container.StartFailed, context, isTransient: true) => ContainerId = containerId;
  }
}
