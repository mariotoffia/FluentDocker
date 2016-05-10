using System;
using System.IO;

namespace Ductus.FluentDocker.Resources
{
  public sealed class ResourceStream : IDisposable
  {
    public ResourceStream(Stream stream, ResourceInfo info)
    {
      Stream = stream;
      Info = info;
    }

    public Stream Stream { get; }
    public ResourceInfo Info { get; }
    public void Dispose()
    {
      Stream.Dispose();
    }
  }
}
