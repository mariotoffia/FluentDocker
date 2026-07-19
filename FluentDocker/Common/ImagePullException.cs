#nullable enable
using System;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Common
{
  /// <summary>
  /// Exception thrown when an image pull operation fails.
  /// </summary>
  public class ImagePullException : DriverException
  {
    private static readonly string[] NonTransientReasons =
    [
      "unauthorized",
      "denied",
      "manifest unknown",
      "not found"
    ];

    /// <summary>
    /// The name of the image that failed to pull.
    /// </summary>
    public string ImageName { get; }

    /// <summary>
    /// Initializes a new instance with the specified image name and reason.
    /// </summary>
    /// <param name="imageName">The name of the image that failed to pull.</param>
    /// <param name="reason">The reason the pull operation failed.</param>
    /// <param name="isTransient">Whether retrying the pull may succeed.</param>
    public ImagePullException(string imageName, string reason, bool isTransient = true)
        : base($"Failed to pull image '{imageName}': {reason}", ErrorCodes.Image.PullFailed, null, isTransient: isTransient) => ImageName = imageName;

    /// <summary>
    /// Initializes a new instance with the specified image name, reason, and error context.
    /// </summary>
    /// <param name="imageName">The name of the image that failed to pull.</param>
    /// <param name="reason">The reason the pull operation failed.</param>
    /// <param name="context">Diagnostic context information.</param>
    /// <param name="isTransient">Whether retrying the pull may succeed.</param>
    public ImagePullException(string imageName, string reason, ErrorContext? context, bool isTransient = true)
        : base($"Failed to pull image '{imageName}': {reason}", ErrorCodes.Image.PullFailed, context, isTransient: isTransient) => ImageName = imageName;

    /// <summary>
    /// Initializes a new instance with the specified image name, reason, and inner exception.
    /// </summary>
    /// <param name="imageName">The name of the image that failed to pull.</param>
    /// <param name="reason">The reason the pull operation failed.</param>
    /// <param name="innerException">The exception that caused the pull failure.</param>
    /// <param name="isTransient">Whether retrying the pull may succeed.</param>
    public ImagePullException(string imageName, string reason, Exception? innerException, bool isTransient = true)
        : base($"Failed to pull image '{imageName}': {reason}", ErrorCodes.Image.PullFailed, null, innerException, isTransient: isTransient) => ImageName = imageName;

    /// <summary>
    /// Classifies a registry pull reason for retry policies.
    /// </summary>
    /// <param name="reason">The pull failure reason.</param>
    /// <returns><c>false</c> for permanent auth/not-found failures; otherwise <c>true</c>.</returns>
    public static bool IsTransientReason(string? reason)
    {
      if (string.IsNullOrWhiteSpace(reason))
        return true;

      foreach (var nonTransientReason in NonTransientReasons)
      {
        if (reason.Contains(nonTransientReason, StringComparison.OrdinalIgnoreCase))
          return false;
      }

      return true;
    }
  }
}
