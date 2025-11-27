using System.IO;
using System.Text;
using Ductus.FluentDocker.Model.Common;

namespace Ductus.FluentDocker.Extensions
{
  public static class FileExtensions
  {
    public static string EscapePath(this string path)
    {
      if (string.IsNullOrEmpty(path) || -1 == path.IndexOf(' '))
        return path;

      return path.StartsWith("\"") ? path : $"\"{path}\"";
    }

    public static TemplateString EscapePath(this TemplateString path)
    {
      if (string.IsNullOrEmpty(path))
        return path;

      var p = path.Rendered;
      if (-1 == p.IndexOf(' '))
        return path;

      return p.StartsWith("\"") ? path : new TemplateString($"\"{p}\"");
    }

    public static void ToFile(this string contents, TemplateString fqPath)
    {
      var folder = Path.GetDirectoryName(fqPath.Rendered.EscapePath());
      if (null != folder && !Directory.Exists(folder))
      {
        Directory.CreateDirectory(folder);
      }

      File.WriteAllText(fqPath.Rendered.EscapePath(), contents);
    }

    public static string FromFile(this TemplateString fqPath, Encoding encoding = null)
    {
      if (null == encoding)
      {
        encoding = Encoding.UTF8;
      }

      return File.ReadAllText(fqPath.Rendered.EscapePath(), encoding);
    }

    /// <summary>
    ///   Copies file or directories (recursively) to the <paramref name="workdir" /> and returns a relative
    ///   linux compatible¨path string to be used in e.g. a Dockerfile.
    /// </summary>
    /// <param name="fileOrDirectory">The file or directory to copy to <paramref name="workdir" />.</param>
    /// <param name="workdir">The working directory to copy the file or directory to.</param>
    /// <returns>A relative path to <paramref name="workdir" /> in linux format. If fails it will return null.</returns>
    /// <remarks>
    ///   If the <paramref name="fileOrDirectory" /> is on format emb://namespace/file format it will use
    ///   <see
    ///     cref="ResourceExtensions.ToFile(System.Collections.Generic.IEnumerable{Ductus.FluentDocker.Resources.ResourceInfo},TemplateString)" />
    ///   to perform the copy. Only one file is permitted and thus the file or directory parameter is always a single file.
    /// </remarks>
    public static string Copy(this TemplateString fileOrDirectory, TemplateString workdir)
    {
      var fd = fileOrDirectory.Rendered.EscapePath();

      if (fd.StartsWith($"{EmbeddedUri.Prefix}:"))
      {
        return new EmbeddedUri(fd).ToFile(workdir);
      }

      if (File.Exists(fd))
      {
        var file = Path.GetFileName(fd);
        File.Copy(fd, Path.Combine(workdir, file));
        return file;
      }

      if (!Directory.Exists(fd))
      {
        return null;
      }

      CopyTo(fd, workdir);

      // Return the relative path of workdir
      return Path.GetFileName(Path.GetFullPath(fd).TrimEnd(Path.DirectorySeparatorChar));
    }

    public static void CopyTo(this TemplateString sourceDirectory, TemplateString targetDirectory)
    {
      var sd = sourceDirectory.Rendered.EscapePath();
      var td = targetDirectory.Rendered.EscapePath();

      CopyAll(new DirectoryInfo(sd), new DirectoryInfo(td));
    }

    private static void CopyAll(DirectoryInfo source, DirectoryInfo target)
    {
      Directory.CreateDirectory(target.FullName);

      foreach (var fi in source.GetFiles())
      {
        fi.CopyTo(Path.Combine(target.FullName, fi.Name), true);
      }

      foreach (var diSourceSubDir in source.GetDirectories())
      {
        var nextTargetSubDir =
          target.CreateSubdirectory(diSourceSubDir.Name);
        CopyAll(diSourceSubDir, nextTargetSubDir);
      }
    }
  }
}
