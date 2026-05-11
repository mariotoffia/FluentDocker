using System;
using FluentDocker.Common;

namespace FluentDocker.Services
{
  /// <summary>
  /// Thrown when a service operation is not supported by the current driver or platform.
  /// </summary>
  public class FluentDockerNotSupportedException : FluentDockerException
  {
    /// <summary>Creates a new instance with no message.</summary>
    public FluentDockerNotSupportedException()
    {
    }

    /// <summary>Creates a new instance with the specified message.</summary>
    public FluentDockerNotSupportedException(string message) : base(message)
    {
    }

    /// <summary>Creates a new instance with the specified message and inner exception.</summary>
    public FluentDockerNotSupportedException(string message, Exception innerException) : base(message, innerException)
    {
    }
  }
}
