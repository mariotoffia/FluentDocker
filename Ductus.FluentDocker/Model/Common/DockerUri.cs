using System;
using System.Runtime.Serialization;

namespace Ductus.FluentDocker.Model.Common
{
  public sealed class DockerUri : Uri
  {
    public DockerUri(string uriString) : base(uriString)
    {
    }

    public DockerUri(SerializationInfo serializationInfo, StreamingContext streamingContext)
      : base(serializationInfo, streamingContext)
    {
    }
  }
}