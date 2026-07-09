using System;
using System.Formats.Tar;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Model.Drivers;
using Microsoft.Extensions.Logging;

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
          // Defense-in-depth: EnumerateFiles(AllDirectories) follows directory symlinks, so a crafted
          // archive that plants an in-tree symlink resolving out of tree could surface an out-of-tree
          // file here. The "multiple files" guard above usually pre-empts it (following the symlink
          // expands the listing past one entry), but reject explicitly rather than rely on that.
          if (!IsPlainRegularFileWithinRoot(Path.GetFullPath(extractDir), file))
            throw new InvalidOperationException(
                "Docker archive single-file entry resolves outside the extraction root");
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
        using var reader = new TarReader(readFs, leaveOpen: true);
        while (await reader.GetNextEntryAsync(copyData: false, cancellationToken)
            .ConfigureAwait(false) is { } entry)
        {
          var target = GetSafeArchiveTarget(root, rootWithSeparator, entry.Name);
          // GetSafeArchiveTarget only checks lexical containment. Before creating anything AT target,
          // reject any entry whose parent chain reaches destination through a symlinked directory
          // component: a crafted archive composes two lexically-innocent symlinks (x->'.', x/climb->'..')
          // to make an in-tree path physically resolve outside the tree, turning a plain file write,
          // mkdir, symlink, or hardlink copy into an out-of-tree write/read (CWE-59). One guard here
          // covers every branch because every entry routes through this target.
          EnsureNoSymlinkInParentChain(root, target);
          // A duplicate-named earlier entry may have planted a symlink AT the leaf target; both
          // FileStream(FileMode.Create) and File.Copy(overwrite:true) FOLLOW a leaf symlink and would
          // write through it, out of tree (CWE-59). Unlink any pre-existing reparse point at target
          // first (deleting a symlink removes the link, never its target) - tar's unlink-before-extract.
          // Real files/dirs are left untouched for normal overwrite semantics.
          RemoveLeafSymlink(root, target);
          switch (entry.EntryType)
          {
            case TarEntryType.Directory:
              Directory.CreateDirectory(target);
              continue;
            case TarEntryType.SymbolicLink:
              logger.LogWarning(
                  "Skipping Docker archive link entry dereference; preserving symlink '{Entry}' with target '{Target}' during CopyFrom extraction",
                  entry.Name, entry.LinkName);
              PreserveSymlink(target, entry.LinkName, root, rootWithSeparator, logger);
              continue;
            case TarEntryType.HardLink:
              // A hardlink's LinkName is the archive-root-relative name of an already-extracted
              // regular-file entry. Materialize it as a copy when it resolves to an in-tree file.
              // TryResolveExtractedHardLinkSource walks the SOURCE's parent chain (via
              // IsPlainRegularFileWithinRoot) because the destination guard above only protects writes:
              // a crafted archive can plant an in-tree symlink with a clean parent chain whose SOURCE
              // path still resolves out of tree, so the source needs its own reparse-point check.
              if (TryResolveExtractedHardLinkSource(root, rootWithSeparator, entry.LinkName,
                  out var hardLinkSource))
              {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(hardLinkSource, target, overwrite: true);
                ApplyUnixFileMode(target, entry);
              }
              else
              {
                logger.LogWarning(
                    "Skipping Docker archive hardlink '{Entry}': target '{Target}' did not resolve to a safe in-tree file",
                    entry.Name, entry.LinkName);
              }
              continue;
            case TarEntryType.RegularFile:
            case TarEntryType.V7RegularFile:
            case TarEntryType.ContiguousFile:
              break;
            default:
              continue;
          }
          Directory.CreateDirectory(Path.GetDirectoryName(target)!);
          await using (var output = new FileStream(
              target, FileMode.Create, FileAccess.Write, FileShare.None,
              bufferSize: 81920, FileOptions.Asynchronous))
          {
            if (entry.DataStream is { } data)
              await data.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
          }
          ApplyUnixFileMode(target, entry);
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

    private static bool TryResolveExtractedHardLinkSource(
        string root, string rootWithSeparator, string linkTarget, out string source)
    {
      source = null!;
      if (string.IsNullOrEmpty(linkTarget) || Path.IsPathRooted(linkTarget))
        return false;
      var candidate = Path.GetFullPath(Path.Combine(rootWithSeparator, linkTarget.TrimStart('/')));
      if (!string.Equals(candidate, root, StringComparison.Ordinal) &&
          !candidate.StartsWith(rootWithSeparator, StringComparison.Ordinal))
        return false;
      if (!IsPlainRegularFileWithinRoot(root, candidate))
        return false;
      source = candidate;
      return true;
    }

    // A file we are about to READ from staging (a hardlink source, or the single extracted file) is only
    // safe if its on-disk path neither IS a symlink nor descends through one from root. The lexical
    // containment check above collapses '..' as string ops and never resolves on-disk symlinks, so a
    // crafted archive can pre-create an in-root symlink whose own parent chain is clean yet which
    // physically resolves out of tree (CWE-59); File.Copy/FileStream would then read an out-of-tree file.
    // This complements EnsureNoSymlinkInParentChain, which only guards WRITE destinations. FileSystemInfo
    // reports the link's own attributes (never the target's), so the reparse flag is visible on the walk.
    private static bool IsPlainRegularFileWithinRoot(string root, string candidate)
    {
      var info = new FileInfo(candidate);
      if (!info.Exists || (info.Attributes & FileAttributes.ReparsePoint) != 0)
        return false;
      for (var dir = info.Directory; dir != null; dir = dir.Parent)
      {
        var full = dir.FullName.TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(full, root, StringComparison.Ordinal))
          return true;
        if (dir.Exists && (dir.Attributes & FileAttributes.ReparsePoint) != 0)
          return false;
      }
      return false;
    }

    // Reject any target whose parent chain, up to root, traverses an EXISTING symlinked directory
    // component. GetSafeArchiveTarget's containment check is purely lexical (Path.GetFullPath collapses
    // '..' as string ops, never resolving on-disk symlinks), so a crafted archive can pre-create an
    // in-root symlink (via an earlier link entry) that lexically stays in-tree but physically resolves
    // out of tree; writing/reading through it escapes the staging boundary (CWE-59). FileSystemInfo
    // reports the link's own attributes, never the target's, so the walk sees the reparse point. A
    // well-formed container archive never writes through a symlinked component, so throwing (consistent
    // with GetSafeArchiveTarget's own escape guard) fails closed without harming legitimate archives.
    private static void EnsureNoSymlinkInParentChain(string root, string target)
    {
      if (string.Equals(target, root, StringComparison.Ordinal))
        return;
      for (var dir = new DirectoryInfo(Path.GetDirectoryName(target)!); dir != null; dir = dir.Parent)
      {
        var full = dir.FullName.TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(full, root, StringComparison.Ordinal))
          return;
        if (dir.Exists && (dir.Attributes & FileAttributes.ReparsePoint) != 0)
          throw new InvalidOperationException(
              $"Docker archive entry writes through a symlinked directory component: {target}");
      }
      throw new InvalidOperationException($"Docker archive entry escapes destination: {target}");
    }

    // Unlink a symlink planted at the leaf target by a duplicate-named earlier entry, so the subsequent
    // write does not follow it out of tree. Only reparse points are removed (deleting a symlink unlinks
    // the link, never its target); real files/dirs are left for normal FileMode.Create/overwrite:true.
    private static void RemoveLeafSymlink(string root, string target)
    {
      if (string.Equals(target, root, StringComparison.Ordinal))
        return;
      if (!TryGetFileAttributes(target, out var attributes) ||
          (attributes & FileAttributes.ReparsePoint) == 0)
        return;
      if ((attributes & FileAttributes.Directory) != 0)
        Directory.Delete(target);
      else
        File.Delete(target);
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

    private static void ApplyUnixFileMode(string target, TarEntry entry)
    {
      if (OperatingSystem.IsWindows())
        return;
      var mode = entry.Mode & (UnixFileMode)511;
      if (mode == 0)
        return;
      try
      {
        File.SetUnixFileMode(target, mode);
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
