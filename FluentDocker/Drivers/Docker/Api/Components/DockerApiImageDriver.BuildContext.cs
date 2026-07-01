using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
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
        // Canonicalise both the link and the context root through the real filesystem so
        // the containment decision matches what the kernel does when the file is opened.
        // A purely lexical check (Path.GetFullPath / ResolveLinkTarget) cancels "symlink/.."
        // textually and so lets a link escape through an in-context directory symlink
        // (e.g. dir -> /etc, link -> dir/../passwd resolves lexically inside but reads /etc/passwd).
        var realRoot = RealPath(contextRoot);
        var realFile = RealPath(file.FullName);
        if (realFile is null || realRoot is null)
          return true; // broken / unresolvable link => exclude (safe default)

        var root = realRoot.EndsWith(Path.DirectorySeparatorChar)
            ? realRoot
            : realRoot + Path.DirectorySeparatorChar;
        return realFile != realRoot && !realFile.StartsWith(root, StringComparison.Ordinal);
      }
      catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
      {
        return true; // exclude (safe default)
      }
    }

    /// <summary>
    /// Canonicalises <paramref name="path"/> through the real filesystem the way the kernel
    /// resolves it on open: symlinks (including intermediate directory symlinks) are followed
    /// and <c>..</c> is applied AFTER symlink resolution. This is required for a containment
    /// decision because <see cref="Path.GetFullPath(string)"/> and <c>ResolveLinkTarget</c>
    /// collapse <c>symlink/..</c> lexically and so disagree with the kernel exactly where it
    /// matters. Returns <c>null</c> when a component cannot be resolved (missing target,
    /// broken link, symlink loop).
    /// </summary>
    private static string? RealPath(string path)
    {
      if (OperatingSystem.IsWindows())
      {
        // ponytail: Windows containment is best-effort (leaf-resolve + lexical) — the Docker
        // API driver targets a Linux daemon and NTFS symlink/junction creation needs elevation.
        // Upgrade to GetFinalPathNameByHandle if hostile contexts on a Windows daemon matter.
        try
        {
          var resolved = new FileInfo(path).ResolveLinkTarget(returnFinalTarget: true);
          return Path.GetFullPath(resolved?.FullName ?? path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
          return null;
        }
      }

      var ptr = Realpath(ToNullTerminatedUtf8(path), IntPtr.Zero);
      if (ptr == IntPtr.Zero)
        return null;
      try
      {
        return Marshal.PtrToStringUTF8(ptr);
      }
      finally
      {
        Free(ptr);
      }
    }

    private static byte[] ToNullTerminatedUtf8(string path)
    {
      var utf8 = Encoding.UTF8.GetBytes(path);
      var buffer = new byte[utf8.Length + 1]; // libc realpath expects a null-terminated char*
      Array.Copy(utf8, buffer, utf8.Length);
      return buffer;
    }

    // realpath(3) with a NULL buffer allocates the result (POSIX.1-2008; glibc and macOS
    // libc both support it). The returned buffer must be released with free(3). The path is
    // marshalled as a UTF-8 byte[] rather than a string so no ANSI string marshaling is used.
    [DllImport("libc", EntryPoint = "realpath")]
    private static extern IntPtr Realpath(byte[] path, IntPtr resolved);

    [DllImport("libc", EntryPoint = "free")]
    private static extern void Free(IntPtr ptr);

    #endregion
  }
}
