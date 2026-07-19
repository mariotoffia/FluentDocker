#nullable enable
using System.IO;

namespace FluentDocker.Resources
{
  /// <summary>
  /// Persists embedded-resource content read via <see cref="ResourceReader"/>/<see cref="ResourceStream"/>
  /// to a destination (see <see cref="FileResourceWriter"/> for the filesystem implementation).
  /// </summary>
  public interface IResourceWriter
  {
    /// <summary>Writes a single resource's content.</summary>
    /// <param name="stream">The open resource stream to persist; not disposed by this call.</param>
    /// <returns>This writer, for chaining.</returns>
    IResourceWriter Write(ResourceStream stream);

    /// <summary>Writes every resource yielded by <paramref name="resources"/>, disposing each as it completes.</summary>
    /// <param name="resources">The resources to persist.</param>
    /// <returns>This writer, for chaining.</returns>
    IResourceWriter Write(ResourceReader resources);
  }
}
