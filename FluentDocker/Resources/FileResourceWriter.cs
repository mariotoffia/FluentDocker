#nullable enable
using System;
using System.IO;
using FluentDocker.Model.Common;

namespace FluentDocker.Resources
{
  /// <summary>
  /// Writes embedded resources out as files under <paramref name="basePath"/>, recreating each
  /// resource's <see cref="ResourceInfo.RelativeRootNamespace"/> as subfolders. Writes are atomic:
  /// content is staged to a randomly-named temp file in the target directory and moved into place,
  /// so a reader never observes a partially-written file.
  /// </summary>
  /// <param name="basePath">The base directory resources are written under; created on demand.</param>
  public sealed class FileResourceWriter(TemplateString basePath) : IResourceWriter
  {
    private readonly TemplateString _basePath = basePath;

    /// <summary>
    /// Writes <paramref name="stream"/>'s content to a file under the configured base path, named after
    /// <see cref="ResourceInfo.Resource"/> and nested per <see cref="ResourceInfo.RelativeRootNamespace"/>.
    /// </summary>
    /// <param name="stream">The resource stream to write; its content is copied, not disposed.</param>
    /// <returns>This writer, for chaining.</returns>
    /// <exception cref="ArgumentException"><see cref="ResourceInfo.Resource"/> is not a single relative file name.</exception>
    public IResourceWriter Write(ResourceStream stream)
    {
      var dir = string.IsNullOrEmpty(stream.Info.RelativeRootNamespace)
        ? _basePath.Rendered
        : Path.Combine(_basePath.Rendered, stream.Info.RelativeRootNamespace.Replace('.', Path.DirectorySeparatorChar));

      if (!Directory.Exists(dir))
      {
        Directory.CreateDirectory(dir);
      }

      var destination = Path.Combine(dir, SafeResourceName(stream.Info.Resource));
      var temp = Path.Combine(dir, Path.GetRandomFileName());
      try
      {
        using (var fileStream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
          stream.Stream.CopyTo(fileStream);
          fileStream.Flush(true);
        }

        File.Move(temp, destination, true);
      }
      finally
      {
        if (File.Exists(temp))
          File.Delete(temp);
      }

      return this;
    }

    private static string SafeResourceName(string? resource)
    {
      if (string.IsNullOrWhiteSpace(resource)
          || Path.IsPathRooted(resource)
          || resource.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal)
          || resource.Contains(Path.AltDirectorySeparatorChar, StringComparison.Ordinal)
          || resource == "."
          || resource == "..")
        throw new ArgumentException("Resource name must be a single relative file name.", nameof(resource));

      return resource;
    }

    /// <summary>Writes every resource in <paramref name="resources"/> to a file, disposing each stream as it completes.</summary>
    /// <param name="resources">The resources to write.</param>
    /// <returns>This writer, for chaining.</returns>
    public IResourceWriter Write(ResourceReader resources)
    {
      foreach (var resource in resources)
      {
        try
        {
          Write(resource);
        }
        finally
        {
          resource.Dispose();
        }
      }

      return this;
    }
  }
}
