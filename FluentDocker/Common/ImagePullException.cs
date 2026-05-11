using System;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Common
{
  /// <summary>
  /// Exception thrown when an image pull operation fails.
  /// </summary>
  public class ImagePullException : DriverException
  {
    /// <summary>
    /// The name of the image that failed to pull.
    /// </summary>
    public string ImageName { get; }

    /// <summary>
    /// Initializes a new instance with the specified image name and reason.
    /// </summary>
    /// <param name="imageName">The name of the image that failed to pull.</param>
    /// <param name="reason">The reason the pull operation failed.</param>
    public ImagePullException(string imageName, string reason)
        : base($"Failed to pull image '{imageName}': {reason}", ErrorCodes.Image.PullFailed, null, isTransient: true) => ImageName = imageName;

    /// <summary>
    /// Initializes a new instance with the specified image name, reason, and error context.
    /// </summary>
    /// <param name="imageName">The name of the image that failed to pull.</param>
    /// <param name="reason">The reason the pull operation failed.</param>
    /// <param name="context">Diagnostic context information.</param>
    public ImagePullException(string imageName, string reason, ErrorContext context)
        : base($"Failed to pull image '{imageName}': {reason}", ErrorCodes.Image.PullFailed, context, isTransient: true) => ImageName = imageName;

    /// <summary>
    /// Initializes a new instance with the specified image name, reason, and inner exception.
    /// </summary>
    /// <param name="imageName">The name of the image that failed to pull.</param>
    /// <param name="reason">The reason the pull operation failed.</param>
    /// <param name="innerException">The exception that caused the pull failure.</param>
    public ImagePullException(string imageName, string reason, Exception innerException)
        : base($"Failed to pull image '{imageName}': {reason}", ErrorCodes.Image.PullFailed, null, innerException, isTransient: true) => ImageName = imageName;
  }
}
