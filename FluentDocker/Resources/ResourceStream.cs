using System;
using System.IO;

namespace FluentDocker.Resources
{
#pragma warning disable CA1711 // Type name ends in 'Stream' — intentional, wraps a Stream resource
  public sealed class ResourceStream(Stream stream, ResourceInfo info) : IDisposable
#pragma warning restore CA1711
  {
    public Stream Stream { get; } = stream;
    public ResourceInfo Info { get; } = info;
    public void Dispose()
    {
      Stream.Dispose();
      GC.SuppressFinalize(this);
    }
  }
}
