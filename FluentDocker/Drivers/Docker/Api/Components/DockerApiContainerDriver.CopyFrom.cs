using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Model.Drivers;
using Microsoft.Extensions.Logging;
using SharpCompress.Common;
using SharpCompress.Common.Tar;
using SharpCompress.Readers;

namespace FluentDocker.Drivers.Docker.Api.Components
{
  public partial class DockerApiContainerDriver
  {
    private async Task<CommandResponse<Unit>> CopyFromArchiveAsync(
        DriverContext context, string containerId, string containerPath, string hostPath,
        CancellationToken cancellationToken)
    {
      try
      {
        var apiPath = $"/containers/{Uri.EscapeDataString(containerId)}" +
                      $"/archive?path={Uri.EscapeDataString(containerPath)}";
        using var stream = await GetRawStreamAsync(apiPath, cancellationToken).ConfigureAwait(false);
        var targetIsDirectory = Directory.Exists(hostPath) || EndsWithDirectorySeparator(hostPath);
        if (targetIsDirectory)
        {
          await ExtractArchiveToDirectoryAsync(stream, hostPath, Logger, cancellationToken)
              .ConfigureAwait(false);
          return CommandResponse<Unit>.Ok(Unit.Default);
        }

        var parent = Path.GetDirectoryName(Path.GetFullPath(hostPath));
        if (!string.IsNullOrEmpty(parent))
          Directory.CreateDirectory(parent);

        var extractDir = Path.Combine(parent ?? ".", $".fluentdocker-copy-{Guid.NewGuid():N}");
        try
        {
          await ExtractArchiveToDirectoryAsync(stream, extractDir, Logger, cancellationToken)
              .ConfigureAwait(false);
          var files = Directory.EnumerateFiles(extractDir, "*", SearchOption.AllDirectories)
              .Take(2).ToList();
          if (files.Count == 0)
            throw new InvalidOperationException("Docker archive contained no file");
          if (files.Count > 1)
            throw new InvalidOperationException("Docker archive contained multiple files; copy to a directory path instead");
          var file = files[0];
          await using var source = new FileStream(
              file, FileMode.Open, FileAccess.Read, FileShare.Read,
              bufferSize: 81920, FileOptions.Asynchronous);
          await using var destination = new FileStream(
              hostPath, FileMode.Create, FileAccess.Write, FileShare.None,
              bufferSize: 81920, FileOptions.Asynchronous);
          await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
          if (Directory.Exists(extractDir))
            Directory.Delete(extractDir, recursive: true);
        }

        return CommandResponse<Unit>.Ok(Unit.Default);
      }
      catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(
            $"Failed to copy from container '{containerId}': {ex.Message}",
            ErrorCodes.Container.CopyFailed,
            CreateErrorContext($"GET /containers/{containerId}/archive", 0));
      }
    }

    private static async Task ExtractArchiveToDirectoryAsync(
        Stream stream, string directory, ILogger logger, CancellationToken cancellationToken)
    {
      var destination = Path.GetFullPath(directory).TrimEnd(
          Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
      var parent = Path.GetDirectoryName(destination) ?? ".";
      Directory.CreateDirectory(parent);
      var staging = Path.Combine(parent, $".fluentdocker-extract-{Guid.NewGuid():N}");
      Directory.CreateDirectory(staging);
      var root = Path.GetFullPath(staging).TrimEnd(
          Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
      var rootWithSeparator = root + Path.DirectorySeparatorChar;
      var tmp = Path.Combine(parent, $".fluentdocker-archive-{Guid.NewGuid():N}.tmp");
      try
      {
        await using (var fs = new FileStream(
            tmp, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 81920, FileOptions.Asynchronous))
        {
          await stream.CopyToAsync(fs, cancellationToken).ConfigureAwait(false);
        }

        await using var readFs = new FileStream(
            tmp, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 81920, FileOptions.Asynchronous);
        using var reader = ReaderFactory.OpenReader(readFs);
        while (reader.MoveToNextEntry())
        {
          var target = GetSafeArchiveTarget(root, rootWithSeparator, reader.Entry.Key);
          if (!string.IsNullOrEmpty(reader.Entry.LinkTarget))
          {
            logger.LogWarning(
                "Skipping Docker archive link entry dereference; preserving symlink '{Entry}' with target '{Target}' during CopyFrom extraction",
                reader.Entry.Key, reader.Entry.LinkTarget);
            PreserveSymlink(target, reader.Entry.LinkTarget, root, rootWithSeparator, logger);
            continue;
          }
          if (reader.Entry.IsDirectory)
            continue;
          Directory.CreateDirectory(Path.GetDirectoryName(target)!);
          await using var entry = reader.OpenEntryStream();
          await using (var output = new FileStream(
              target, FileMode.Create, FileAccess.Write, FileShare.None,
              bufferSize: 81920, FileOptions.Asynchronous))
          {
            await entry.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
          }
          ApplyUnixFileMode(target, reader.Entry);
        }

        if (Directory.Exists(destination))
          MoveDirectoryContents(staging, destination);
        else
          Directory.Move(staging, destination);
      }
      finally
      {
        try
        {
          File.Delete(tmp);
        }
        catch (Exception)
        {
        }
        try
        {
          if (Directory.Exists(staging))
            Directory.Delete(staging, recursive: true);
        }
        catch (Exception)
        {
        }
      }
    }

    private static string GetSafeArchiveTarget(string root, string rootWithSeparator, string key)
    {
      var target = Path.GetFullPath(Path.Combine(rootWithSeparator, key));
      if (!string.Equals(target, root, StringComparison.Ordinal) &&
          !target.StartsWith(rootWithSeparator, StringComparison.Ordinal))
        throw new InvalidOperationException($"Docker archive entry escapes destination: {key}");
      return target;
    }

    private static void PreserveSymlink(
        string target, string linkTarget, string root, string rootWithSeparator, ILogger logger)
    {
      if (Path.IsPathRooted(linkTarget))
        throw new InvalidOperationException($"Docker archive link target escapes destination: {linkTarget}");
      var parent = Path.GetDirectoryName(target)!;
      var resolved = Path.GetFullPath(Path.Combine(parent, linkTarget));
      if (!string.Equals(resolved, root, StringComparison.Ordinal) &&
          !resolved.StartsWith(rootWithSeparator, StringComparison.Ordinal))
        throw new InvalidOperationException($"Docker archive link target escapes destination: {linkTarget}");
      Directory.CreateDirectory(parent);
      try
      {
        if (Directory.Exists(resolved))
          Directory.CreateSymbolicLink(target, linkTarget);
        else
          File.CreateSymbolicLink(target, linkTarget);
      }
      catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
      {
        logger.LogWarning(ex,
            "Could not preserve Docker archive symlink '{Entry}' with target '{Target}'",
            target, linkTarget);
      }
    }

    private static void ApplyUnixFileMode(string target, IEntry entry)
    {
      if (OperatingSystem.IsWindows() || entry is not TarEntry tarEntry)
        return;
      var mode = (int)tarEntry.Mode & 511;
      if (mode == 0)
        return;
      try
      {
        File.SetUnixFileMode(target, (UnixFileMode)mode);
      }
      catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
      {
      }
    }

    private static void MoveDirectoryContents(string source, string destination)
    {
      // ponytail: extraction is atomic (destination is only touched after a full, successful
      // extract into the sibling staging dir), but this same-filesystem rename merge is best-effort
      // per file — a failure mid-move can leave the destination partially updated. Stage-and-swap the
      // whole directory if all-or-nothing on the merge phase ever matters.
      foreach (var entry in Directory.EnumerateFileSystemEntries(source))
      {
        var target = Path.Combine(destination, Path.GetFileName(entry));
        var attributes = File.GetAttributes(entry);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
          MoveReparsePoint(entry, target, attributes);
          continue;
        }
        if ((attributes & FileAttributes.Directory) != 0)
        {
          Directory.CreateDirectory(target);
          MoveDirectoryContents(entry, target);
          continue;
        }
        File.Move(entry, target, overwrite: true);
      }
    }

    private static void MoveReparsePoint(string entry, string target, FileAttributes attributes)
    {
      if ((attributes & FileAttributes.Directory) != 0)
      {
        Directory.Move(entry, target);
        return;
      }

      var linkTarget = new FileInfo(entry).LinkTarget ?? new DirectoryInfo(entry).LinkTarget;
      if (linkTarget == null)
      {
        File.Move(entry, target, overwrite: true);
        return;
      }

      DeleteExistingFileOrSymlink(target);
      File.CreateSymbolicLink(target, linkTarget);
      File.Delete(entry);
    }

    private static void DeleteExistingFileOrSymlink(string target)
    {
      if (!TryGetFileAttributes(target, out var attributes))
        return;
      if ((attributes & FileAttributes.Directory) != 0 &&
          (attributes & FileAttributes.ReparsePoint) == 0)
        return;
      if ((attributes & FileAttributes.Directory) != 0)
        Directory.Delete(target);
      else
        File.Delete(target);
    }

    private static bool TryGetFileAttributes(string target, out FileAttributes attributes)
    {
      try
      {
        attributes = File.GetAttributes(target);
        return true;
      }
      catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
      {
        attributes = default;
        return false;
      }
    }

    private static bool EndsWithDirectorySeparator(string path)
    {
      return path.EndsWith(Path.DirectorySeparatorChar) ||
          path.EndsWith(Path.AltDirectorySeparatorChar);
    }
  }
}
