using System.IO;
using System.Text;
using Ductus.FluentDocker.Model.Builders;
using Ductus.FluentDocker.Model.Common;

namespace Ductus.FluentDocker.Extensions
{
  public static class FileExtensions
  {
    public static void WriteDockerFile(this FileBuilderConfig config, TemplateString buildFolder)
    {
      config.ToString().WriteFile(buildFolder);
    }

    public static void WriteFile(this string contents, TemplateString folder)
    {
      if (!Directory.Exists(folder))
      {
        Directory.CreateDirectory(folder);
      }

      File.WriteAllText(Path.Combine(folder, "Dockerfile"), contents);
    }

    public static string ReadFile(this TemplateString fqPath, Encoding encoding = null)
    {
      if (null == encoding)
      {
        encoding = Encoding.UTF8;
      }

      return File.ReadAllText(fqPath, encoding);
    }
  }
}
