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
          ExtractArchiveToDirectory(stream, hostPath);
          return CommandResponse<Unit>.Ok(Unit.Default);
        }

        var parent = Path.GetDirectoryName(Path.GetFullPath(hostPath));
        if (!string.IsNullOrEmpty(parent))
          Directory.CreateDirectory(parent);

        var extractDir = Path.Combine(parent ?? ".", $".fluentdocker-copy-{Guid.NewGuid():N}");
        try
        {
          ExtractArchiveToDirectory(stream, extractDir);
          var file = Directory.EnumerateFiles(extractDir, "*", SearchOption.AllDirectories)
              .SingleOrDefault()
              ?? throw new InvalidOperationException("Docker archive contained no file");
          File.Copy(file, hostPath, overwrite: true);
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

    private static void ExtractArchiveToDirectory(Stream stream, string directory)
    {
      Directory.CreateDirectory(directory);
      var root = Path.GetFullPath(directory);
      if (!EndsWithDirectorySeparator(root))
        root += Path.DirectorySeparatorChar;
      using var reader = ReaderFactory.OpenReader(stream);
      while (reader.MoveToNextEntry())
      {
        if (reader.Entry.IsDirectory)
          continue;
        var target = Path.GetFullPath(Path.Combine(root, reader.Entry.Key));
        if (!target.StartsWith(root, StringComparison.Ordinal))
          throw new InvalidOperationException($"Docker archive entry escapes destination: {reader.Entry.Key}");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        using var entry = reader.OpenEntryStream();
        using var output = File.Create(target);
        entry.CopyTo(output);
      }
    }

    private static bool EndsWithDirectorySeparator(string path)
    {
      return path.EndsWith(Path.DirectorySeparatorChar) ||
          path.EndsWith(Path.AltDirectorySeparatorChar);
    }
  }
}
