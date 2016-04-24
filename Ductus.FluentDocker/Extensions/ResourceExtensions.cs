using System;
using System.IO;
using System.Reflection;

namespace Ductus.FluentDocker.Extensions
{
  public static class ResourceExtensions
  {
    public static string ExtractEmbeddedResource(this Uri resource, string outputDir)
    {
      var ns = resource.Host;
      var file = resource.LocalPath;

      ExtractEmbeddedResource(ns, outputDir, file);
      return file;
    }

    public static void ExtractEmbeddedResource(this string resourceLocation, string outputDir, params string[] files)
    {
      if (!Directory.Exists(outputDir))
      {
        Directory.CreateDirectory(outputDir);
      }

      foreach (var file in files)
      {
        using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceLocation + @"." + file))
        {
          using (var fileStream = new FileStream(Path.Combine(outputDir, file), FileMode.Create))
          {
            if (null == stream)
            {
              throw new InvalidOperationException(
                $"Could not find stream for {file} in namespace {resourceLocation} to {outputDir}");
            }

            for (var i = 0; i < stream.Length; i++)
            {
              fileStream.WriteByte((byte)stream.ReadByte());
            }
            fileStream.Close();
          }
        }
      }
    }
  }
}
