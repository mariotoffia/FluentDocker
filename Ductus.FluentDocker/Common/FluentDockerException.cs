using System;

namespace Ductus.FluentDocker.Common
{
  public class FluentDockerException : Exception
  {
    public FluentDockerException()
    {
    }

    public FluentDockerException(string message) : base(message)
    {
    }

    public FluentDockerException(string message, Exception innerException) : base(message, innerException)
    {
    }
  }
}
