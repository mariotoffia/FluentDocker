using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
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

        if (File.Exists(hostPath) && !containerPath.EndsWith('/') &&
            !await ContainerPathIsDirectoryAsync(containerId, containerPath, cancellationToken)
                .ConfigureAwait(false))
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
          await using var src = new FileStream(
              file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read,
              bufferSize: 81920, FileOptions.Asynchronous);
          await DockerApiTarWriter.WriteFileAsync(tarStream, tarEntryName ?? file.Name, src,
              file.LastWriteTimeUtc, DockerApiTarWriter.FileModeFor(file.FullName),
              cancellationToken).ConfigureAwait(false);
        }
        else
        {
          var root = new DirectoryInfo(hostPath);
          var entryBase = root.Name;
          await DockerApiTarWriter.WriteDirectoryAsync(tarStream, entryBase,
              root.LastWriteTimeUtc, DockerApiTarWriter.DirectoryModeFor(root.FullName),
              cancellationToken).ConfigureAwait(false);
          await WriteDirectoryToTarAsync(tarStream, hostPath, entryBase, cancellationToken)
              .ConfigureAwait(false);
        }
        await DockerApiTarWriter.FinishAsync(tarStream, cancellationToken).ConfigureAwait(false);

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

    private async Task<bool> ContainerPathIsDirectoryAsync(
        string containerId, string containerPath, CancellationToken cancellationToken)
    {
      var statPath = $"/containers/{Uri.EscapeDataString(containerId)}" +
                     $"/archive?path={Uri.EscapeDataString(containerPath)}";
      using var response = await Connection.HeadAsync(statPath, cancellationToken)
          .ConfigureAwait(false);
      if (!response.IsSuccessStatusCode ||
          !response.Headers.TryGetValues("X-Docker-Container-Path-Stat", out var values))
        return false;

      var encoded = values.FirstOrDefault();
      if (string.IsNullOrEmpty(encoded))
        return false;
      try
      {
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
        var stat = JsonHelper.ParseElement(json);
        return (stat.GetInt64OrDefault("mode") & 0x80000000L) != 0;
      }
      catch (Exception ex) when (ex is FormatException or JsonException)
      {
        return false;
      }
    }

    private static async Task WriteDirectoryToTarAsync(
        Stream tarStream, string rootDir, string entryBase, CancellationToken cancellationToken)
    {
      foreach (var file in Directory.GetFiles(rootDir).OrderBy(static p => p, StringComparer.Ordinal))
      {
        var info = new FileInfo(file);
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
          continue;
        var entryName = string.IsNullOrEmpty(entryBase)
            ? Path.GetFileName(file) : $"{entryBase}/{Path.GetFileName(file)}";
        await using var src = new FileStream(
            info.FullName, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 81920, FileOptions.Asynchronous);
        await DockerApiTarWriter.WriteFileAsync(tarStream, entryName, src,
            info.LastWriteTimeUtc, DockerApiTarWriter.FileModeFor(info.FullName),
            cancellationToken).ConfigureAwait(false);
      }
      foreach (var dir in Directory.GetDirectories(rootDir).OrderBy(static p => p, StringComparer.Ordinal))
      {
        var info = new DirectoryInfo(dir);
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
          continue;
        var dirName = Path.GetFileName(dir);
        var newBase = string.IsNullOrEmpty(entryBase)
            ? dirName : $"{entryBase}/{dirName}";
        await DockerApiTarWriter.WriteDirectoryAsync(tarStream, newBase,
            info.LastWriteTimeUtc, DockerApiTarWriter.DirectoryModeFor(info.FullName),
            cancellationToken).ConfigureAwait(false);
        await WriteDirectoryToTarAsync(tarStream, dir, newBase, cancellationToken)
            .ConfigureAwait(false);
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
