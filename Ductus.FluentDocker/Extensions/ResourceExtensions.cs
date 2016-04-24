using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Ductus.FluentDocker.Extensions
{
  public static class ResourceExtensions
  {
    public static string ExtractEmbeddedResourceByUri(this string resource, string outputDir)
    {
      var s = resource.Split(':')[1].Split('/');
      Assembly assembly = AppDomain.CurrentDomain.GetAssemblies().First(x => x.GetName().Name == s[0]);
      ExtractEmbeddedResource(s[1],assembly, outputDir, s[2]);
      return s[2];
    }

    public static void ExtractEmbeddedResource(this string resourceLocation, Assembly assembly, string outputDir, params string[] files)
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
              fileStream.WriteByte((byte)stream.ReadByte());
            }
            fileStream.Close();
          }
        }
      }
    }
  }
}
