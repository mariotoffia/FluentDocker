using System;
using Ductus.FluentDocker.Model.Drivers;

namespace Ductus.FluentDocker.Common
{
    /// <summary>
    /// Exception thrown when an image is not found.
    /// </summary>
    public class ImageNotFoundException : DriverException
    {
        public string ImageName { get; }

        public ImageNotFoundException(string imageName)
            : base($"Image '{imageName}' not found", ErrorCodes.Image.NotFound)
        {
            ImageName = imageName;
        }

        public ImageNotFoundException(string imageName, ErrorContext context)
            : base($"Image '{imageName}' not found", ErrorCodes.Image.NotFound, context)
        {
            ImageName = imageName;
        }
    }
}
