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
    /// <exception cref="Common.FluentDockerException"><see cref="ResourceInfo.RelativeRootNamespace"/> maps to a rooted or traversing path fragment.</exception>
    public IResourceWriter Write(ResourceStream stream)
    {
      var dir = string.IsNullOrEmpty(stream.Info.RelativeRootNamespace)
        ? _basePath.Rendered
        : Path.Combine(_basePath.Rendered, SafeRelativeFragment(stream.Info.RelativeRootNamespace));

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
        // Best-effort cleanup: a failing delete (e.g. AV lock) must not replace the original
        // CopyTo/Move exception with its own.
        try
        {
          if (File.Exists(temp))
            File.Delete(temp);
        }
        catch
        {
          // Ignored — the staged temp file is orphaned but the real failure propagates.
        }
      }

      return this;
    }

    /// <summary>
    /// Maps a dotted namespace to a relative directory fragment, rejecting rooted or traversing
    /// results. Hardening for crafted <see cref="ResourceInfo"/> values / unusual manifest names:
    /// a leading dot maps to a leading separator (a rooted fragment makes <see cref="Path.Combine(string, string)"/>
    /// discard the base path) and dot-dot segments would escape it.
    /// </summary>
    private static string SafeRelativeFragment(string relativeRootNamespace)
    {
      var fragment = relativeRootNamespace.Replace('.', Path.DirectorySeparatorChar);
      if (Path.IsPathRooted(fragment))
        throw new Common.FluentDockerException(
          $"Resource namespace '{relativeRootNamespace}' maps to a rooted path and cannot be written under the base path.");

      foreach (var segment in fragment.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
      {
        if (segment.Length == 0 || segment == "." || segment == "..")
          throw new Common.FluentDockerException(
            $"Resource namespace '{relativeRootNamespace}' contains an empty or traversal path segment.");
      }

      return fragment;
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
