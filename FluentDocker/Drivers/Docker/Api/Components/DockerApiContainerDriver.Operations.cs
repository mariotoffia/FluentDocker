using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers.Docker.Api.ApiModels;
using FluentDocker.Model.Drivers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SharpCompress.Common;
using SharpCompress.Readers;
using SharpCompress.Writers;

namespace FluentDocker.Drivers.Docker.Api.Components
{
  /// <summary>
  /// Container operation methods: exec, copy, logs, diff, top, export, rename, update.
  /// </summary>
  public partial class DockerApiContainerDriver
  {
    #region Logs

    /// <summary>Gets logs from a container.</summary>
    public async Task<CommandResponse<string>> GetLogsAsync(
        DriverContext context, string containerId, bool follow = false,
        int? tail = null, bool timestamps = false,
        CancellationToken cancellationToken = default)
    {
      var path = $"/containers/{Uri.EscapeDataString(containerId)}/logs" +
                 $"?stdout=1&stderr=1" +
                 $"&follow={follow.ToString().ToLowerInvariant()}" +
                 $"&timestamps={timestamps.ToString().ToLowerInvariant()}";
      if (tail.HasValue)
        path += $"&tail={tail.Value}";

      try
      {
        using var stream = await GetRawStreamAsync(path, cancellationToken);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var logs = await reader.ReadToEndAsync(cancellationToken);
        return CommandResponse<string>.Ok(StripDockerStreamHeaders(logs));
      }
      catch (Exception ex)
      {
        return CommandResponse<string>.Fail(
            $"Failed to get logs for container '{containerId}': {ex.Message}",
            ErrorCodes.Container.AttachFailed,
            CreateErrorContext($"GET /containers/{containerId}/logs", 0));
      }
    }

    #endregion

    #region Top

    /// <summary>Gets running processes in a container.</summary>
    public async Task<CommandResponse<ContainerProcesses>> TopAsync(
        DriverContext context, string containerId,
        string psOptions = null, CancellationToken cancellationToken = default)
    {
      var path = $"/containers/{Uri.EscapeDataString(containerId)}/top";
      if (!string.IsNullOrEmpty(psOptions))
        path += $"?ps_args={Uri.EscapeDataString(psOptions)}";

      var result = await GetJsonAsync<JObject>(path, cancellationToken);
      if (!result.Success)
        return CommandResponse<ContainerProcesses>.Fail(result.ErrorMessage,
            MapNotFoundErrorCode(result.StatusCode, ErrorCodes.Container.TopFailed),
            CreateErrorContext($"GET /containers/{containerId}/top",
                result.StatusCode, result.ResponseBody),
            result.StatusCode);

      var processes = new ContainerProcesses();
      if (result.Data?["Titles"] is JArray titles)
        processes.Titles = titles.Select(t => t.Value<string>()).ToList();
      if (result.Data?["Processes"] is JArray rows)
      {
        processes.Processes = rows
            .Select(row => row is JArray cols
                ? cols.Select(c => c.Value<string>()).ToList()
                : new List<string>())
            .ToList();
      }
      return CommandResponse<ContainerProcesses>.Ok(processes);
    }

    #endregion

    #region Diff

    /// <summary>Shows changes to a container's filesystem.</summary>
    public async Task<CommandResponse<IList<FilesystemChange>>> DiffAsync(
        DriverContext context, string containerId,
        CancellationToken cancellationToken = default)
    {
      var path = $"/containers/{Uri.EscapeDataString(containerId)}/changes";
      var result = await GetJsonAsync<JArray>(path, cancellationToken);
      if (!result.Success)
        return CommandResponse<IList<FilesystemChange>>.Fail(result.ErrorMessage,
            MapNotFoundErrorCode(result.StatusCode, ErrorCodes.Container.DiffFailed),
            CreateErrorContext($"GET /containers/{containerId}/changes",
                result.StatusCode, result.ResponseBody),
            result.StatusCode);

      var changes = new List<FilesystemChange>();
      if (result.Data != null)
      {
        foreach (var item in result.Data)
        {
          changes.Add(new FilesystemChange
          {
            Path = item.Value<string>("Path"),
            Kind = MapDiffKind(item.Value<int>("Kind"))
          });
        }
      }
      return CommandResponse<IList<FilesystemChange>>.Ok(changes);
    }

    /// <summary>Maps Docker API integer diff kind: 0=Modified(C), 1=Added(A), 2=Deleted(D).</summary>
    private static string MapDiffKind(int kind) => kind switch
    {
      0 => "C",
      1 => "A",
      2 => "D",
      _ => "C"
    };

    #endregion

    #region Exec

    /// <summary>
    /// Executes a command in a running container using three-phase exec API:
    /// create exec instance, start exec, inspect exec for exit code.
    /// </summary>
    public async Task<CommandResponse<ExecResult>> ExecAsync(
        DriverContext context, string containerId, ExecConfig config,
        CancellationToken cancellationToken = default)
    {
      // Phase 1: Create exec instance
      var createRequest = new ExecCreateRequest
      {
        Cmd = config.Command,
        WorkingDir = config.WorkingDir,
        User = config.User,
        Privileged = config.Privileged,
        Tty = config.Tty,
        AttachStdin = config.Interactive,
        AttachStdout = !config.Detach,
        AttachStderr = !config.Detach,
        Detach = config.Detach,
        Env = config.Environment?.Count > 0
              ? config.Environment.Select(kv => $"{kv.Key}={kv.Value}").ToArray()
              : null
      };

      var createPath = $"/containers/{Uri.EscapeDataString(containerId)}/exec";
      var createResult = await PostJsonAsync<ExecCreateResponse>(
          createPath, createRequest, cancellationToken);
      if (!createResult.Success)
        return CommandResponse<ExecResult>.Fail(createResult.ErrorMessage,
            MapNotFoundErrorCode(createResult.StatusCode, ErrorCodes.Container.ExecFailed),
            CreateErrorContext($"POST /containers/{containerId}/exec",
                createResult.StatusCode, createResult.ResponseBody),
            createResult.StatusCode);

      var execId = createResult.Data?.Id;
      if (string.IsNullOrEmpty(execId))
        return CommandResponse<ExecResult>.Fail(
            "Exec create returned empty ID", ErrorCodes.Container.ExecFailed);

      // Phase 2: Start exec and capture output
      var startRequest = new ExecStartRequest { Detach = config.Detach, Tty = config.Tty };
      string output;
      try
      {
        var startContent = new StringContent(
            JsonConvert.SerializeObject(startRequest),
            Encoding.UTF8, "application/json");
        using var stream = await Connection.PostStreamAsync(
            $"/exec/{execId}/start", startContent, cancellationToken);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        output = await reader.ReadToEndAsync(cancellationToken);
      }
      catch (Exception ex)
      {
        return CommandResponse<ExecResult>.Fail(
            $"Failed to start exec '{execId}': {ex.Message}",
            ErrorCodes.Container.ExecFailed,
            CreateErrorContext($"POST /exec/{execId}/start", 0));
      }

      // Phase 3: Inspect exec for exit code
      var inspectResult = await GetJsonAsync<ExecInspectResponse>(
          $"/exec/{execId}/json", cancellationToken);
      var exitCode = inspectResult.Success ? inspectResult.Data?.ExitCode ?? -1 : -1;

      return CommandResponse<ExecResult>.Ok(new ExecResult
      {
        ExitCode = exitCode,
        StdOut = output ?? string.Empty,
        StdErr = string.Empty
      });
    }

    #endregion

    #region Copy To Container

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
        using var tarStream = new MemoryStream();
        using (var writer = WriterFactory.Open(tarStream, ArchiveType.Tar,
            new WriterOptions(CompressionType.None)))
        {
          if (File.Exists(hostPath))
            writer.Write(Path.GetFileName(hostPath), hostPath);
          else
            WriteDirectoryToTar(writer, hostPath, string.Empty);
        }

        tarStream.Position = 0;
        var apiPath = $"/containers/{Uri.EscapeDataString(containerId)}" +
                      $"/archive?path={Uri.EscapeDataString(containerPath)}";
        var result = await PutStreamAsync(
            apiPath, tarStream, "application/x-tar", cancellationToken);
        if (!result.Success)
          return CommandResponse<Unit>.Fail(result.ErrorMessage,
              MapNotFoundErrorCode(result.StatusCode, ErrorCodes.Container.CopyFailed),
              CreateErrorContext($"PUT /containers/{containerId}/archive",
                  result.StatusCode, result.ResponseBody),
              result.StatusCode);

        return CommandResponse<Unit>.Ok(Unit.Default);
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(
            $"Failed to copy to container '{containerId}': {ex.Message}",
            ErrorCodes.Container.CopyFailed,
            CreateErrorContext($"PUT /containers/{containerId}/archive", 0));
      }
    }

    private static void WriteDirectoryToTar(IWriter writer, string rootDir, string entryBase)
    {
      foreach (var file in Directory.GetFiles(rootDir))
      {
        var entryName = string.IsNullOrEmpty(entryBase)
            ? Path.GetFileName(file) : $"{entryBase}/{Path.GetFileName(file)}";
        writer.Write(entryName, file);
      }
      foreach (var dir in Directory.GetDirectories(rootDir))
      {
        var dirName = Path.GetFileName(dir);
        var newBase = string.IsNullOrEmpty(entryBase)
            ? dirName : $"{entryBase}/{dirName}";
        WriteDirectoryToTar(writer, dir, newBase);
      }
    }

    #endregion

    #region Copy From Container

    /// <summary>
    /// Copies files from a container to the host by downloading a tar archive
    /// from the container archive API and extracting it.
    /// </summary>
    public async Task<CommandResponse<Unit>> CopyFromAsync(
        DriverContext context, string containerId,
        string containerPath, string hostPath,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var apiPath = $"/containers/{Uri.EscapeDataString(containerId)}" +
                      $"/archive?path={Uri.EscapeDataString(containerPath)}";
        using var stream = await GetRawStreamAsync(apiPath, cancellationToken);
        Directory.CreateDirectory(hostPath);
        using var reader = ReaderFactory.Open(stream);
        while (reader.MoveToNextEntry())
        {
          if (reader.Entry.IsDirectory)
            continue;
          reader.WriteEntryToDirectory(hostPath, new ExtractionOptions
          {
            ExtractFullPath = true,
            Overwrite = true
          });
        }
        return CommandResponse<Unit>.Ok(Unit.Default);
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(
            $"Failed to copy from container '{containerId}': {ex.Message}",
            ErrorCodes.Container.CopyFailed,
            CreateErrorContext($"GET /containers/{containerId}/archive", 0));
      }
    }

    #endregion

    #region Export

    /// <summary>Exports a container's filesystem as a tar archive.</summary>
    public async Task<CommandResponse<Unit>> ExportAsync(
        DriverContext context, string containerId, string outputPath,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var apiPath = $"/containers/{Uri.EscapeDataString(containerId)}/export";
        using var stream = await GetRawStreamAsync(apiPath, cancellationToken);
        var outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDir))
          Directory.CreateDirectory(outputDir);
        await using var fileStream = new FileStream(
            outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await stream.CopyToAsync(fileStream, cancellationToken);
        return CommandResponse<Unit>.Ok(Unit.Default);
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(
            $"Failed to export container '{containerId}': {ex.Message}",
            ErrorCodes.Container.ExportFailed,
            CreateErrorContext($"GET /containers/{containerId}/export", 0));
      }
    }

    #endregion

    #region Rename

    /// <summary>Renames a container.</summary>
    public async Task<CommandResponse<Unit>> RenameAsync(
        DriverContext context, string containerId, string newName,
        CancellationToken cancellationToken = default)
    {
      var path = $"/containers/{Uri.EscapeDataString(containerId)}" +
                 $"/rename?name={Uri.EscapeDataString(newName)}";
      var result = await PostAsync(path, null, cancellationToken);
      if (!result.Success)
        return CommandResponse<Unit>.Fail(result.ErrorMessage,
            MapNotFoundErrorCode(result.StatusCode, ErrorCodes.Container.RenameFailed),
            CreateErrorContext($"POST /containers/{containerId}/rename",
                result.StatusCode, result.ResponseBody),
            result.StatusCode);
      return CommandResponse<Unit>.Ok(Unit.Default);
    }

    #endregion

    #region Update

    /// <summary>Updates a container's resource limits.</summary>
    public async Task<CommandResponse<Unit>> UpdateAsync(
        DriverContext context, string containerId, ContainerUpdateConfig config,
        CancellationToken cancellationToken = default)
    {
      var request = new UpdateContainerRequest
      {
        Memory = config.MemoryLimit,
        MemoryReservation = config.MemoryReservation,
        CpuShares = config.CpuShares,
        CpuPeriod = config.CpuPeriod,
        CpuQuota = config.CpuQuota,
        CpusetCpus = config.CpusetCpus,
        PidsLimit = config.PidsLimit
      };
      if (!string.IsNullOrEmpty(config.RestartPolicy))
        request.RestartPolicy = new RestartPolicyRequest { Name = config.RestartPolicy };

      var path = $"/containers/{Uri.EscapeDataString(containerId)}/update";
      var result = await PostJsonAsync<JObject>(path, request, cancellationToken);
      if (!result.Success)
        return CommandResponse<Unit>.Fail(result.ErrorMessage,
            MapNotFoundErrorCode(result.StatusCode, ErrorCodes.Container.UpdateFailed),
            CreateErrorContext($"POST /containers/{containerId}/update",
                result.StatusCode, result.ResponseBody),
            result.StatusCode);
      return CommandResponse<Unit>.Ok(Unit.Default);
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Strips Docker multiplexed stream 8-byte header frames from raw log output.
    /// Frame format: [1B stream type][3B padding][4B big-endian size][payload].
    /// </summary>
    private static string StripDockerStreamHeaders(string raw)
    {
      if (string.IsNullOrEmpty(raw))
        return string.Empty;

      var bytes = Encoding.UTF8.GetBytes(raw);
      if (bytes.Length < 8)
        return raw;

      // Check if first byte is a valid Docker stream header (0=stdin, 1=stdout, 2=stderr)
      if (bytes[0] > 2 || bytes[1] != 0 || bytes[2] != 0 || bytes[3] != 0)
        return raw;

      var sb = new StringBuilder();
      var offset = 0;
      while (offset + 8 <= bytes.Length)
      {
        var frameSize = (bytes[offset + 4] << 24) | (bytes[offset + 5] << 16)
                      | (bytes[offset + 6] << 8) | bytes[offset + 7];
        offset += 8;
        if (frameSize <= 0 || offset + frameSize > bytes.Length)
          break;
        sb.Append(Encoding.UTF8.GetString(bytes, offset, frameSize));
        offset += frameSize;
      }
      return sb.Length > 0 ? sb.ToString() : raw;
    }

    #endregion
  }
}
