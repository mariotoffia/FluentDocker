using System.IO;
using Ductus.FluentDocker.Model.Common;

namespace Ductus.FluentDocker.Resources
{
  public sealed class FileResourceWriter : IResourceWriter
  {
    private readonly TemplateString _basePath;

    public FileResourceWriter(TemplateString basePath)
    {
      _basePath = basePath;
    }

    public IResourceWriter Write(ResourceStream stream)
    {
      var dir = string.IsNullOrEmpty(stream.Info.RelativeRootNamespace)
        ? _basePath.Rendered
        : Path.Combine(_basePath, stream.Info.RelativeRootNamespace.Replace('.', Path.PathSeparator));

      if (!Directory.Exists(dir))
      {
        Directory.CreateDirectory(dir);
      }

      using (var fileStream = new FileStream(Path.Combine(dir, stream.Info.Resource), FileMode.Create))
      {
        stream.Stream.CopyTo(fileStream);
        fileStream.Flush();
      }

      return this;
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