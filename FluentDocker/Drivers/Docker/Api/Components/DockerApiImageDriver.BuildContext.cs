using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentDocker.Common;

namespace FluentDocker.Drivers.Docker.Api.Components
{
  /// <summary>
  /// Partial class with build-context helpers: tar packaging (streamed to a temp file,
  /// honouring <c>.dockerignore</c>) and the <c>/build</c> query-string assembly.
  /// </summary>
  public partial class DockerApiImageDriver
  {
    #region Build Context

    /// <summary>
    /// Packs the build context directory into a tar archive, streamed to a temp file
    /// (deleted on close) rather than buffered in memory, and filtered through the
    /// context's <c>.dockerignore</c> rules. The caller owns the returned stream.
    /// </summary>
    private static Stream CreateBuildContextTar(string contextPath, ImageBuildConfig config)
    {
      var dockerfileName = string.IsNullOrEmpty(config?.DockerfileName)
          ? "Dockerfile"
          : config.DockerfileName;
      var filter = DockerIgnoreFilter.Load(contextPath, dockerfileName);

      // Temp file with DeleteOnClose: bounds memory regardless of context size and
      // auto-deletes when the caller disposes the stream.
      var tempPath = Path.GetTempFileName();
      var fileStream = new FileStream(
          tempPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None,
          bufferSize: 81920, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
      try
      {
        using (var writer = SharpCompress.Writers.WriterFactory.OpenWriter(
            fileStream, SharpCompress.Common.ArchiveType.Tar,
            new SharpCompress.Writers.Tar.TarWriterOptions(
                SharpCompress.Common.CompressionType.None, true)))
        {
          var contextRoot = Path.GetFullPath(contextPath);
          foreach (var file in EnumerateContextFilesSafe(contextRoot))
          {
            var relativePath = Path.GetRelativePath(contextRoot, file.FullName)
                .Replace('\\', '/');
            if (filter.IsIgnored(relativePath))
              continue;
            try
            {
              using var src = file.OpenRead();
              writer.Write(relativePath, src, file.LastWriteTimeUtc);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
              // Dangling in-context symlink or unreadable file: skip it instead of failing the
              // whole build, mirroring Docker's best-effort context packaging.
            }
          }
        }

        fileStream.Position = 0;
        return fileStream;
      }
      catch
      {
        fileStream.Dispose();
        throw;
      }
    }

    private static List<string> BuildBuildQueryParams(ImageBuildConfig config)
    {
      var query = new List<string>();

      if (!string.IsNullOrEmpty(config.DockerfileName))
        query.Add($"dockerfile={Uri.EscapeDataString(config.DockerfileName)}");

      foreach (var tag in config.Tags ?? Enumerable.Empty<string>())
        query.Add($"t={Uri.EscapeDataString(tag)}");

      if (config.NoCache)
        query.Add("nocache=true");

      if (config.Pull)
        query.Add("pull=true");

      if (!config.Rm)
        query.Add("rm=false");

      if (config.ForceRm)
        query.Add("forcerm=true");

      if (config.Squash)
        query.Add("squash=true");

      if (!string.IsNullOrEmpty(config.Target))
        query.Add($"target={Uri.EscapeDataString(config.Target)}");

      if (!string.IsNullOrEmpty(config.Platform))
        query.Add($"platform={Uri.EscapeDataString(config.Platform)}");

      if (!string.IsNullOrEmpty(config.NetworkMode))
        query.Add($"networkmode={Uri.EscapeDataString(config.NetworkMode)}");

      if (config.Memory.HasValue)
        query.Add($"memory={config.Memory.Value}");

      if (config.CpuQuota.HasValue)
        query.Add($"cpuquota={config.CpuQuota.Value}");

      if (config.BuildArgs?.Count > 0)
      {
        var json = JsonHelper.Serialize(config.BuildArgs);
        query.Add($"buildargs={Uri.EscapeDataString(json)}");
      }

      if (config.Labels?.Count > 0)
      {
        var json = JsonHelper.Serialize(config.Labels);
        query.Add($"labels={Uri.EscapeDataString(json)}");
      }

      return query;
    }

    /// <summary>
    /// Depth-first enumeration of files under the build context that mirrors Docker's
    /// security posture: directory symlinks are not traversed (avoids infinite loops and
    /// paths that escape the context) and file symlinks whose target resolves outside the
    /// context are skipped (avoids exfiltrating host files such as <c>/etc/passwd</c>).
    /// File symlinks that stay inside the context are dereferenced as before. Because no
    /// directory symlink is followed, content under an in-context directory symlink is
    /// included only via its real path (SharpCompress cannot emit symlink tar entries).
    /// </summary>
    private static IEnumerable<FileInfo> EnumerateContextFilesSafe(string contextRoot)
    {
      var stack = new Stack<DirectoryInfo>();
      stack.Push(new DirectoryInfo(contextRoot));
      while (stack.Count > 0)
      {
        foreach (var entry in stack.Pop().EnumerateFileSystemInfos())
        {
          var isSymlink = (entry.Attributes & FileAttributes.ReparsePoint) != 0;
          if (entry is DirectoryInfo dir)
          {
            if (!isSymlink)
              stack.Push(dir);
          }
          else if (entry is FileInfo file && !(isSymlink && EscapesContext(file, contextRoot)))
          {
            yield return file;
          }
        }
      }
    }

    /// <summary>
    /// True when <paramref name="file"/> is a symlink whose fully resolved target lies
    /// outside <paramref name="contextRoot"/>, or cannot be resolved. Such links are
    /// excluded from the build context so they cannot leak host files.
    /// </summary>
    private static bool EscapesContext(FileInfo file, string contextRoot)
    {
      try
      {
        var target = file.ResolveLinkTarget(returnFinalTarget: true);
        if (target is null)
          return false;
        var resolved = Path.GetFullPath(target.FullName);
        var root = contextRoot.EndsWith(Path.DirectorySeparatorChar)
            ? contextRoot
            : contextRoot + Path.DirectorySeparatorChar;
        return !resolved.StartsWith(root, StringComparison.Ordinal)
            && resolved != contextRoot;
      }
      catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
      {
        return true; // broken / unresolvable link => exclude (safe default)
      }
    }

    #endregion
  }
}
