using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Model.Drivers;
using Microsoft.Extensions.Logging;
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
          if (!string.IsNullOrEmpty(reader.Entry.LinkTarget))
          {
            logger.LogWarning(
                "Skipping Docker archive link entry '{Entry}' with target '{Target}' during CopyFrom extraction",
                reader.Entry.Key, reader.Entry.LinkTarget);
            continue;
          }
          if (reader.Entry.IsDirectory)
            continue;
          var target = Path.GetFullPath(Path.Combine(rootWithSeparator, reader.Entry.Key));
          if (!string.Equals(target, root, StringComparison.Ordinal) &&
              !target.StartsWith(rootWithSeparator, StringComparison.Ordinal))
            throw new InvalidOperationException($"Docker archive entry escapes destination: {reader.Entry.Key}");
          Directory.CreateDirectory(Path.GetDirectoryName(target)!);
          await using var entry = reader.OpenEntryStream();
          await using var output = new FileStream(
              target, FileMode.Create, FileAccess.Write, FileShare.None,
              bufferSize: 81920, FileOptions.Asynchronous);
          await entry.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        }

        Directory.CreateDirectory(destination);
        MoveDirectoryContents(staging, destination);
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

    private static void MoveDirectoryContents(string source, string destination)
    {
      // ponytail: extraction is atomic (destination is only touched after a full, successful
      // extract into the sibling staging dir), but this same-filesystem rename merge is best-effort
      // per file — a failure mid-move can leave the destination partially updated. Stage-and-swap the
      // whole directory if all-or-nothing on the merge phase ever matters.
      foreach (var dir in Directory.EnumerateDirectories(source))
      {
        var target = Path.Combine(destination, Path.GetFileName(dir));
        Directory.CreateDirectory(target);
        MoveDirectoryContents(dir, target);
      }

      foreach (var file in Directory.EnumerateFiles(source))
      {
        var target = Path.Combine(destination, Path.GetFileName(file));
        File.Move(file, target, overwrite: true);
      }
    }

    private static bool EndsWithDirectorySeparator(string path)
    {
      return path.EndsWith(Path.DirectorySeparatorChar) ||
          path.EndsWith(Path.AltDirectorySeparatorChar);
    }
  }
}
