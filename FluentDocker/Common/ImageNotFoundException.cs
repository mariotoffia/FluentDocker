using System;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Common
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
