using System;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Common
{
  /// <summary>
  /// Exception thrown when a container is not found.
  /// </summary>
  public class ContainerNotFoundException : DriverException
  {
    /// <summary>
    /// The identifier of the container that was not found.
    /// </summary>
    public string ContainerId { get; }

    /// <summary>
    /// Initializes a new instance with the specified container identifier.
    /// </summary>
    /// <param name="containerId">The identifier of the container that was not found.</param>
    public ContainerNotFoundException(string containerId)
        : base($"Container '{containerId}' not found", ErrorCodes.Container.NotFound) => ContainerId = containerId;

    /// <summary>
    /// Initializes a new instance with the specified container identifier and error context.
    /// </summary>
    /// <param name="containerId">The identifier of the container that was not found.</param>
    /// <param name="context">Diagnostic context information.</param>
    public ContainerNotFoundException(string containerId, ErrorContext context)
        : base($"Container '{containerId}' not found", ErrorCodes.Container.NotFound, context) => ContainerId = containerId;
  }
}
