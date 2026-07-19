#nullable enable
using System;
using System.IO;

namespace FluentDocker.Resources
{
  /// <summary>
  /// Pairs an open <see cref="System.IO.Stream"/> onto an embedded manifest resource with the
  /// <see cref="ResourceInfo"/> describing it, as yielded by <see cref="ResourceReader"/>. Disposing
  /// this instance disposes the underlying stream.
  /// </summary>
  /// <param name="stream">The open stream over the embedded resource's contents.</param>
  /// <param name="info">The metadata describing the resource the stream was opened from.</param>
#pragma warning disable CA1711 // Type name ends in 'Stream' — intentional, wraps a Stream resource
  public sealed class ResourceStream(Stream stream, ResourceInfo info) : IDisposable
#pragma warning restore CA1711
  {
    /// <summary>The open stream over the embedded resource's contents.</summary>
    public Stream Stream { get; } = stream;
    /// <summary>The metadata describing the resource this stream was opened from.</summary>
    public ResourceInfo Info { get; } = info;
    /// <summary>Disposes <see cref="Stream"/>.</summary>
    public void Dispose()
    {
      Stream.Dispose();
      GC.SuppressFinalize(this);
    }
  }
}
