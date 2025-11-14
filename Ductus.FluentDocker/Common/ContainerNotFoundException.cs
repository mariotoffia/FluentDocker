using System;
using Ductus.FluentDocker.Model.Drivers;

namespace Ductus.FluentDocker.Common
{
    /// <summary>
    /// Exception thrown when a container is not found.
    /// </summary>
    public class ContainerNotFoundException : DriverException
    {
        public string ContainerId { get; }

        public ContainerNotFoundException(string containerId)
            : base($"Container '{containerId}' not found", ErrorCodes.Container.NotFound)
        {
            ContainerId = containerId;
        }

        public ContainerNotFoundException(string containerId, ErrorContext context)
            : base($"Container '{containerId}' not found", ErrorCodes.Container.NotFound, context)
        {
            ContainerId = containerId;
        }
    }
}
