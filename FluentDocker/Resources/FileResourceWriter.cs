#nullable enable
using System;
using System.IO;
using FluentDocker.Model.Common;

namespace FluentDocker.Resources
{
  public sealed class FileResourceWriter(TemplateString basePath) : IResourceWriter
  {
    private readonly TemplateString _basePath = basePath;

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
