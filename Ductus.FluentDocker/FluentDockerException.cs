using System;
using System.Runtime.Serialization;

namespace Ductus.FluentDocker
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

    protected FluentDockerException(SerializationInfo info, StreamingContext context) : base(info, context)
    {
    }
  }
}
