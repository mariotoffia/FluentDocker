#nullable enable
using System;
using System.IO;
using System.Text;
using FluentDocker.Model.Common;

namespace FluentDocker.Extensions
{
  /// <summary>
  /// File and path helpers used by the legacy builder extension surface.
  /// </summary>
  public static class FileExtensions
  {
    /// <summary>
    /// Wraps a path in quotes when it contains spaces.
    /// </summary>
    /// <param name="path">The path to wrap.</param>
    /// <returns>The original path, or a quoted path when it contains spaces.</returns>
    [Obsolete("Use FluentDocker.Common.CommandLineQuoting for command arguments; this legacy helper only wraps paths containing spaces.")]
    public static string EscapePath(this string path)
    {
      if (string.IsNullOrEmpty(path) || !path.Contains(' '))
        return path;

      return path.StartsWith('"') ? path : $"\"{path}\"";
    }

    /// <summary>
    /// Wraps a rendered template path in quotes when it contains spaces.
    /// </summary>
    /// <param name="path">The template path to wrap.</param>
    /// <returns>The original template, or a quoted template when it contains spaces.</returns>
    [Obsolete("Use FluentDocker.Common.CommandLineQuoting for command arguments; this legacy helper only wraps paths containing spaces.")]
    public static TemplateString EscapePath(this TemplateString path)
    {
      if (string.IsNullOrEmpty(path))
        return path;

      var p = path.Rendered;
      if (!p.Contains(' '))
        return path;

      return p.StartsWith('"') ? path : new TemplateString($"\"{p}\"");
    }

    /// <summary>
    /// Writes text to a file, creating the parent directory when needed.
    /// </summary>
    /// <param name="contents">The text to write.</param>
    /// <param name="fqPath">The destination file path.</param>
    public static void ToFile(this string contents, TemplateString fqPath)
    {
      var folder = Path.GetDirectoryName(fqPath.Rendered);
      if (null != folder && !Directory.Exists(folder))
      {
        Directory.CreateDirectory(folder);
      }

      File.WriteAllText(fqPath.Rendered, contents);
    }

    /// <summary>
    /// Reads all text from a file.
    /// </summary>
    /// <param name="fqPath">The file path to read.</param>
    /// <param name="encoding">The encoding to use, or UTF-8 when omitted.</param>
    /// <returns>The file contents.</returns>
    public static string FromFile(this TemplateString fqPath, Encoding? encoding = null)
    {
      if (null == encoding)
      {
        encoding = Encoding.UTF8;
      }

      return File.ReadAllText(fqPath.Rendered, encoding);
    }

    /// <summary>
    ///   Copies file or directories recursively to the <paramref name="workdir" /> and returns a relative
    ///   Linux-compatible path string to use in e.g. a Dockerfile.
    /// </summary>
    /// <param name="fileOrDirectory">The file or directory to copy to <paramref name="workdir" />.</param>
    /// <param name="workdir">The working directory to copy the file or directory to.</param>
    /// <returns>A relative path to <paramref name="workdir" /> in linux format. If fails it will return null.</returns>
    /// <remarks>
    ///   If the <paramref name="fileOrDirectory" /> is on format emb://namespace/file format it will use
    ///   <see
    ///     cref="ResourceExtensions.ToFile(System.Collections.Generic.IEnumerable{FluentDocker.Resources.ResourceInfo},TemplateString)" />
    ///   to perform the copy. Only one file is permitted and thus the file or directory parameter is always a single file.
    /// </remarks>
    public static string? Copy(this TemplateString fileOrDirectory, TemplateString workdir)
    {
      var fd = fileOrDirectory.Rendered;

      if (fd.StartsWith($"{EmbeddedUri.Prefix}:", StringComparison.OrdinalIgnoreCase))
      {
        return new EmbeddedUri(fd).ToFile(workdir);
      }

      if (File.Exists(fd))
      {
        var file = Path.GetFileName(fd);
        File.Copy(fd, Path.Combine(workdir.Rendered, file), true);
        return file;
      }

      if (!Directory.Exists(fd))
      {
        return null;
      }

      var name = Path.GetFileName(Path.GetFullPath(fd).TrimEnd(Path.DirectorySeparatorChar));
      CopyTo(new TemplateString(fd), new TemplateString(Path.Combine(workdir.Rendered, name)));

      return name;
    }

    /// <summary>
    /// Copies a directory recursively to another directory.
    /// </summary>
    /// <param name="sourceDirectory">The source directory.</param>
    /// <param name="targetDirectory">The target directory.</param>
    public static void CopyTo(this TemplateString sourceDirectory, TemplateString targetDirectory)
    {
      var sd = sourceDirectory.Rendered;
      var td = targetDirectory.Rendered;

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
