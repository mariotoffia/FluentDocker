using System;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Common
{
  /// <summary>
  /// Exception thrown when an image is not found.
  /// </summary>
  public class ImageNotFoundException : DriverException
  {
    /// <summary>
    /// The name of the image that was not found.
    /// </summary>
    public string ImageName { get; }

    /// <summary>
    /// Initializes a new instance with the specified image name.
    /// </summary>
    /// <param name="imageName">The name of the image that was not found.</param>
    public ImageNotFoundException(string imageName)
        : base($"Image '{imageName}' not found", ErrorCodes.Image.NotFound) => ImageName = imageName;

    /// <summary>
    /// Initializes a new instance with the specified image name and error context.
    /// </summary>
    /// <param name="imageName">The name of the image that was not found.</param>
    /// <param name="context">Diagnostic context information.</param>
    public ImageNotFoundException(string imageName, ErrorContext context)
        : base($"Image '{imageName}' not found", ErrorCodes.Image.NotFound, context) => ImageName = imageName;
  }
}
