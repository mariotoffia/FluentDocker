using System;
using Ductus.FluentDocker.Common;

namespace Ductus.FluentDocker.Services
{
  public class FluentDockerNotSupportedException : FluentDockerException
  {
    public FluentDockerNotSupportedException()
    {
    }

    public FluentDockerNotSupportedException(string message) : base(message)
    {
    }

    public FluentDockerNotSupportedException(string message, Exception innerException) : base(message, innerException)
    {
    }
  }
}