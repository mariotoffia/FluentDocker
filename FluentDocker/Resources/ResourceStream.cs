using System;
using System.IO;

namespace FluentDocker.Resources
{
#pragma warning disable CA1711 // Type name ends in 'Stream' — intentional, wraps a Stream resource
  public sealed class ResourceStream : IDisposable
#pragma warning restore CA1711
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
      GC.SuppressFinalize(this);
    }
  }
}
