using System;
using Ductus.FluentDocker.Model.Drivers;

namespace Ductus.FluentDocker.Common
{
    /// <summary>
    /// Exception thrown when an image pull operation fails.
    /// </summary>
    public class ImagePullException : DriverException
    {
        public string ImageName { get; }

        public ImagePullException(string imageName, string reason)
            : base($"Failed to pull image '{imageName}': {reason}", ErrorCodes.Image.PullFailed, null, isTransient: true)
        {
            ImageName = imageName;
        }

        public ImagePullException(string imageName, string reason, ErrorContext context)
            : base($"Failed to pull image '{imageName}': {reason}", ErrorCodes.Image.PullFailed, context, isTransient: true)
        {
            ImageName = imageName;
        }

        public ImagePullException(string imageName, string reason, Exception innerException)
            : base($"Failed to pull image '{imageName}': {reason}", ErrorCodes.Image.PullFailed, null, innerException, isTransient: true)
        {
            ImageName = imageName;
        }
    }
}
