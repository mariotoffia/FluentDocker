using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Drivers.Docker.Api.Components
{
  public partial class DockerApiContainerDriver
  {
    /// <summary>
    /// Copies files from the host to a container by creating a tar archive
    /// and uploading it via the container archive API.
    /// </summary>
    public async Task<CommandResponse<Unit>> CopyToAsync(
        DriverContext context, string containerId,
        string hostPath, string containerPath,
        CancellationToken cancellationToken = default)
    {
      if (!File.Exists(hostPath) && !Directory.Exists(hostPath))
        return CommandResponse<Unit>.Fail(
            $"Host path '{hostPath}' does not exist",
            ErrorCodes.General.InvalidArgument);
      try
      {
        var extractPath = containerPath;
        string tarEntryName = null;

        if (File.Exists(hostPath) && !containerPath.EndsWith('/'))
        {
          var parentDir = containerPath.Contains('/')
              ? containerPath[..containerPath.LastIndexOf('/')]
              : "/";
          if (string.IsNullOrEmpty(parentDir))
            parentDir = "/";
          tarEntryName = containerPath[(containerPath.LastIndexOf('/') + 1)..];
          extractPath = parentDir;
        }

        using var tarStream = CreateTempTarStream();
        if (File.Exists(hostPath))
        {
          var file = new FileInfo(hostPath);
          using var src = file.OpenRead();
          DockerApiTarWriter.WriteFile(tarStream, tarEntryName ?? file.Name, src,
              file.LastWriteTimeUtc, DockerApiTarWriter.FileModeFor(file.FullName));
        }
        else
        {
          WriteDirectoryToTar(tarStream, hostPath, string.Empty);
        }
        DockerApiTarWriter.Finish(tarStream);

        tarStream.Position = 0;
        var apiPath = $"/containers/{Uri.EscapeDataString(containerId)}" +
                      $"/archive?path={Uri.EscapeDataString(extractPath)}";
        var result = await PutStreamAsync(
            apiPath, tarStream, "application/x-tar", cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<Unit>.Fail(result.ErrorMessage,
              MapNotFoundErrorCode(result.StatusCode, ErrorCodes.Container.CopyFailed),
              CreateErrorContext($"PUT /containers/{containerId}/archive",
                  result.StatusCode, result.ResponseBody),
              result.StatusCode);

        return CommandResponse<Unit>.Ok(Unit.Default);
      }
      catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(
            $"Failed to copy to container '{containerId}': {ex.Message}",
            ErrorCodes.Container.CopyFailed,
            CreateErrorContext($"PUT /containers/{containerId}/archive", 0));
      }
    }

    private static void WriteDirectoryToTar(Stream tarStream, string rootDir, string entryBase)
    {
      foreach (var file in Directory.GetFiles(rootDir).OrderBy(static p => p, StringComparer.Ordinal))
      {
        var info = new FileInfo(file);
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
          continue;
        var entryName = string.IsNullOrEmpty(entryBase)
            ? Path.GetFileName(file) : $"{entryBase}/{Path.GetFileName(file)}";
        using var src = info.OpenRead();
        DockerApiTarWriter.WriteFile(tarStream, entryName, src,
            info.LastWriteTimeUtc, DockerApiTarWriter.FileModeFor(info.FullName));
      }
      foreach (var dir in Directory.GetDirectories(rootDir).OrderBy(static p => p, StringComparer.Ordinal))
      {
        var info = new DirectoryInfo(dir);
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
          continue;
        var dirName = Path.GetFileName(dir);
        var newBase = string.IsNullOrEmpty(entryBase)
            ? dirName : $"{entryBase}/{dirName}";
        DockerApiTarWriter.WriteDirectory(tarStream, newBase,
            info.LastWriteTimeUtc, DockerApiTarWriter.DirectoryModeFor(info.FullName));
        WriteDirectoryToTar(tarStream, dir, newBase);
      }
    }

    private static FileStream CreateTempTarStream()
    {
      var tempPath = Path.GetTempFileName();
      return new FileStream(
          tempPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None,
          bufferSize: 81920, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
    }
  }
}
