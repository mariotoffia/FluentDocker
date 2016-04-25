using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Ductus.FluentDocker.Model.Common;

namespace Ductus.FluentDocker.Extensions
{
  public static class ResourceExtensions
  {
    public static string ExtractEmbeddedResourceByUri(this EmbeddedUri resource, string outputDir)
    {
      var assembly = AppDomain.CurrentDomain.GetAssemblies().First(x => x.GetName().Name == resource.Assembly);
      ExtractEmbeddedResource(resource.Namespace, assembly, outputDir, resource.Resource);
      return resource.Resource;
    }

    public static void ExtractEmbeddedResource(this string resourceLocation, Assembly assembly, string outputDir,
      params string[] files)
    {
      if (!Directory.Exists(outputDir))
      {
        Directory.CreateDirectory(outputDir);
      }

      if (null == assembly)
      {
        assembly = Assembly.GetCallingAssembly();
      }

      foreach (var file in files)
      {
        using (var stream = assembly.GetManifestResourceStream(resourceLocation + "." + file))
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
              fileStream.WriteByte((byte) stream.ReadByte());
            }
            fileStream.Close();
          }
        }
      }
    }
  }
}