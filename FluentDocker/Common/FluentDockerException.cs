using System;

namespace FluentDocker.Common
{
  /// <summary>
  /// Base exception for all FluentDocker errors.
  /// </summary>
  public class FluentDockerException : Exception
  {
    /// <summary>Creates a new instance with no message.</summary>
    public FluentDockerException()
    {
    }

    /// <summary>Creates a new instance with the specified message.</summary>
    public FluentDockerException(string message) : base(message)
    {
    }

    /// <summary>Creates a new instance with the specified message and inner exception.</summary>
    public FluentDockerException(string message, Exception innerException) : base(message, innerException)
    {
    }
  }
}
