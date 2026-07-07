#nullable enable
using System;

namespace FluentDocker.Common
{
  /// <summary>
  /// Exception thrown when the selected Docker-compatible runtime is unavailable.
  /// </summary>
  public sealed class FluentDockerUnavailableException : FluentDockerException
  {
    /// <summary>Creates a new instance with no message.</summary>
    public FluentDockerUnavailableException()
    {
    }

    /// <summary>Creates a new instance with the specified message.</summary>
    public FluentDockerUnavailableException(string message) : base(message)
    {
    }

    /// <summary>Creates a new instance with the specified message and inner exception.</summary>
    public FluentDockerUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
  }
}
