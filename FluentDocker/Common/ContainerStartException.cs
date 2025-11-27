using System;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Common
{
    /// <summary>
    /// Exception thrown when a container fails to start.
    /// </summary>
    public class ContainerStartException : DriverException
    {
        public string ContainerId { get; }

        public ContainerStartException(string message)
            : base(message, ErrorCodes.Container.StartFailed, null, isTransient: true)
        {
        }

        public ContainerStartException(string containerId, string reason, ErrorContext context)
            : base($"Failed to start container '{containerId}': {reason}", ErrorCodes.Container.StartFailed, context, isTransient: true)
        {
            ContainerId = containerId;
        }
    }
}
