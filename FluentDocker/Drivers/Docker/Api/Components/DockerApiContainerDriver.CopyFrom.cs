using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Model.Drivers;
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
          await ExtractArchiveToDirectoryAsync(stream, hostPath, cancellationToken)
              .ConfigureAwait(false);
          return CommandResponse<Unit>.Ok(Unit.Default);
        }

        var parent = Path.GetDirectoryName(Path.GetFullPath(hostPath));
        if (!string.IsNullOrEmpty(parent))
          Directory.CreateDirectory(parent);

        var extractDir = Path.Combine(parent ?? ".", $".fluentdocker-copy-{Guid.NewGuid():N}");
        try
        {
          await ExtractArchiveToDirectoryAsync(stream, extractDir, cancellationToken)
              .ConfigureAwait(false);
          var files = Directory.EnumerateFiles(extractDir, "*", SearchOption.AllDirectories)
              .Take(2).ToList();
          if (files.Count == 0)
            throw new InvalidOperationException("Docker archive contained no file");
          if (files.Count > 1)
            throw new InvalidOperationException("Docker archive contained 2 files; copy to a directory path instead");
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
        Stream stream, string directory, CancellationToken cancellationToken)
    {
      Directory.CreateDirectory(directory);
      var root = Path.GetFullPath(directory).TrimEnd(
          Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
      var rootWithSeparator = root + Path.DirectorySeparatorChar;
      using var reader = ReaderFactory.OpenReader(stream);
      while (reader.MoveToNextEntry())
      {
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
    }

    private static bool EndsWithDirectorySeparator(string path)
    {
      return path.EndsWith(Path.DirectorySeparatorChar) ||
          path.EndsWith(Path.AltDirectorySeparatorChar);
    }
  }
}
