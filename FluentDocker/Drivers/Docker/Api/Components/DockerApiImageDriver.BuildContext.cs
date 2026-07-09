using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;

namespace FluentDocker.Drivers.Docker.Api.Components
{
  /// <summary>
  /// Partial class with build-context helpers: tar packaging (streamed to a temp file,
  /// honouring <c>.dockerignore</c>) and the <c>/build</c> query-string assembly.
  /// </summary>
  public partial class DockerApiImageDriver
  {
    private const int RealPathBufferSize = 4096;

    #region Build Context

    /// <summary>
    /// Packs the build context directory into a tar archive, streamed to a temp file
    /// (deleted on close) rather than buffered in memory, and filtered through the
    /// context's <c>.dockerignore</c> rules. The caller owns the returned stream.
    /// </summary>
    private static async Task<Stream> CreateBuildContextTarAsync(
        string contextPath, ImageBuildConfig config, CancellationToken cancellationToken)
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
        var contextRoot = Path.GetFullPath(contextPath);
        foreach (var dir in EnumerateContextDirectoriesSafe(contextRoot))
        {
          var relativePath = Path.GetRelativePath(contextRoot, dir.FullName)
              .Replace('\\', '/');
          if (filter.IsIgnored(relativePath + "/"))
            continue;
          await DockerApiTarWriter.WriteDirectoryAsync(fileStream, relativePath,
              dir.LastWriteTimeUtc, DockerApiTarWriter.DirectoryModeFor(dir.FullName),
              cancellationToken).ConfigureAwait(false);
        }

        foreach (var file in EnumerateContextFilesSafe(contextRoot))
        {
          var relativePath = Path.GetRelativePath(contextRoot, file.FullName)
              .Replace('\\', '/');
          if (filter.IsIgnored(relativePath))
            continue;
          try
          {
            if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
            {
              var linkTarget = GetContainedSymlinkTarget(file, contextRoot);
              if (linkTarget == null)
                continue;
              await DockerApiTarWriter.WriteSymlinkAsync(fileStream, relativePath, linkTarget,
                  file.LastWriteTimeUtc, cancellationToken).ConfigureAwait(false);
              continue;
            }

            await using var src = new FileStream(
                file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 81920, FileOptions.Asynchronous);
            var openedPath = GetContainedOpenedPath(src, file.FullName, contextRoot);
            if (openedPath == null)
              continue;
            await DockerApiTarWriter.WriteFileAsync(fileStream, relativePath, src,
                file.LastWriteTimeUtc, DockerApiTarWriter.FileModeFor(openedPath),
                cancellationToken).ConfigureAwait(false);
          }
          catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
          {
            // Dangling in-context symlink or unreadable file: skip it instead of failing the
            // whole build, mirroring Docker's best-effort context packaging.
          }
        }
        await DockerApiTarWriter.FinishAsync(fileStream, cancellationToken).ConfigureAwait(false);

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
    /// File symlinks that stay inside the context and use relative targets are emitted as
    /// symlink entries. Because no directory symlink is followed, content under an
    /// in-context directory symlink is included only via its real path.
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
          else if (entry is FileInfo file)
          {
            yield return file;
          }
        }
      }
    }

    private static IEnumerable<DirectoryInfo> EnumerateContextDirectoriesSafe(string contextRoot)
    {
      var stack = new Stack<DirectoryInfo>();
      stack.Push(new DirectoryInfo(contextRoot));
      while (stack.Count > 0)
      {
        foreach (var entry in stack.Pop().EnumerateFileSystemInfos())
        {
          var isSymlink = (entry.Attributes & FileAttributes.ReparsePoint) != 0;
          if (entry is DirectoryInfo dir && !isSymlink)
          {
            yield return dir;
            stack.Push(dir);
          }
        }
      }
    }

    private static string? GetContainedOpenedPath(FileStream stream, string path, string contextRoot)
    {
      try
      {
        var realRoot = RealPath(contextRoot);
        var realFile = RealPath(stream, path);
        if (realFile is null || realRoot is null)
          return null;

        return IsContainedRealPath(realFile, realRoot) ? realFile : null;
      }
      catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
      {
        return null;
      }
    }

    private static string? GetContainedSymlinkTarget(FileInfo file, string contextRoot)
    {
      var linkTarget = file.LinkTarget;
      if (string.IsNullOrEmpty(linkTarget) || Path.IsPathRooted(linkTarget))
        return null;

      try
      {
        var realRoot = RealPath(contextRoot);
        var realTarget = RealPath(file.FullName);
        return IsContainedRealPath(realTarget, realRoot) ? linkTarget.Replace('\\', '/') : null;
      }
      catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
      {
        return null;
      }
    }

    private static bool IsContainedRealPath(string? realFile, string? realRoot)
    {
      if (realFile is null || realRoot is null)
        return false;
      var root = realRoot.EndsWith(Path.DirectorySeparatorChar)
          ? realRoot
          : realRoot + Path.DirectorySeparatorChar;
      return realFile == realRoot || realFile.StartsWith(root, StringComparison.Ordinal);
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

      var buffer = Marshal.AllocHGlobal(RealPathBufferSize);
      try
      {
        var ptr = Realpath(ToNullTerminatedUtf8(path), buffer);
        return ptr == IntPtr.Zero ? null : PtrToUtf8String(buffer, RealPathBufferSize);
      }
      finally
      {
        Marshal.FreeHGlobal(buffer);
      }
    }

    private static string? RealPath(FileStream stream, string fallbackPath)
    {
      if (OperatingSystem.IsWindows())
      {
        // ponytail: Windows containment is best-effort — lexical after open. Use
        // GetFinalPathNameByHandle if hostile Windows contexts become a target.
        return Path.GetFullPath(fallbackPath);
      }

      if (OperatingSystem.IsLinux())
        return RealPath($"/proc/self/fd/{stream.SafeFileHandle.DangerousGetHandle().ToInt64()}");

      // ponytail: macOS lacks Linux's /proc/self/fd path; this is best-effort after open.
      // F_GETPATH would close the symlink-swap race if hostile local build contexts matter.
      return RealPath(fallbackPath);
    }

    private static string PtrToUtf8String(IntPtr ptr, int maxBytes)
    {
      var bytes = new byte[maxBytes];
      Marshal.Copy(ptr, bytes, 0, bytes.Length);
      var length = Array.IndexOf(bytes, (byte)0);
      return Encoding.UTF8.GetString(bytes, 0, length < 0 ? bytes.Length : length);
    }

    private static byte[] ToNullTerminatedUtf8(string path)
    {
      var utf8 = Encoding.UTF8.GetBytes(path);
      var buffer = new byte[utf8.Length + 1]; // libc realpath expects a null-terminated char*
      Array.Copy(utf8, buffer, utf8.Length);
      return buffer;
    }

    // realpath(3) writes into the caller-owned buffer. The path is marshalled as a UTF-8
    // byte[] rather than a string so no ANSI string marshaling is used.
    [DllImport("libc", EntryPoint = "realpath")]
    private static extern IntPtr Realpath(byte[] path, IntPtr resolved);

    #endregion
  }
}
