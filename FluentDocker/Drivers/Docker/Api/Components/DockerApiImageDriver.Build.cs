using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers.Docker.Api.ApiModels;
using FluentDocker.Drivers.Docker.Api.Connection;
using FluentDocker.Model.Drivers;
using Newtonsoft.Json;
using SharpCompress.Common;
using SharpCompress.Writers;
using SharpCompress.Writers.Tar;

namespace FluentDocker.Drivers.Docker.Api.Components
{
  /// <summary>
  /// Partial class extending DockerApiImageDriver with streaming build operations:
  /// pull, push, and build.
  /// </summary>
  public partial class DockerApiImageDriver
  {
    #region Pull

    /// <summary>
    /// Pulls an image from a registry via POST /images/create.
    /// Streams NDJSON progress and reports via IProgress.
    /// </summary>
    public partial async Task<CommandResponse<Unit>> PullAsync(
        DriverContext context, string image, string tag,
        IProgress<ImagePullProgress> progress,
        CancellationToken cancellationToken)
    {
      tag ??= "latest";
      var path = $"/images/create" +
                 $"?fromImage={Uri.EscapeDataString(image)}" +
                 $"&tag={Uri.EscapeDataString(tag)}";

      // Use PostStreamAsync directly so we can detect connection failures
      Stream stream;
      try
      {
        stream = await Connection.PostStreamAsync(path, null, cancellationToken);
      }
      catch (HttpRequestException ex)
      {
        return CommandResponse<Unit>.Fail(
            $"Pull failed: {ex.Message}",
            ErrorCodes.Image.PullFailed,
            CreateErrorContext("POST /images/create (pull)", 0));
      }

      string lastError = null;
      var receivedProgress = false;
      using var reader = new StreamReader(stream, Encoding.UTF8);
      while (!cancellationToken.IsCancellationRequested)
      {
        string line;
        try
        { line = await reader.ReadLineAsync(cancellationToken); }
        catch { break; }
        if (line == null)
          break;
        if (string.IsNullOrWhiteSpace(line))
          continue;

        receivedProgress = true;
        var parsed = JsonConvert.DeserializeObject<PullProgressLine>(line);
        if (parsed == null)
          continue;

        if (!string.IsNullOrWhiteSpace(parsed.Error))
        {
          lastError = parsed.Error;
          break;
        }

        progress?.Report(new ImagePullProgress
        {
          Status = parsed.Status,
          Progress = parsed.Progress,
          Id = parsed.Id,
          Current = parsed.ProgressDetail?.Current ?? 0,
          Total = parsed.ProgressDetail?.Total ?? 0
        });
      }

      if (!string.IsNullOrWhiteSpace(lastError))
        return CommandResponse<Unit>.Fail(lastError,
            ErrorCodes.Image.PullFailed,
            CreateErrorContext("POST /images/create (pull)", 0));

      if (!receivedProgress)
        return CommandResponse<Unit>.Fail(
            $"Pull received no response from Docker daemon for '{image}:{tag}'",
            ErrorCodes.Image.PullFailed,
            CreateErrorContext("POST /images/create (pull)", 0));

      return CommandResponse<Unit>.Ok(Unit.Default);
    }

    #endregion

    #region Push

    /// <summary>
    /// Pushes an image to a registry via POST /images/{name}/push.
    /// Streams NDJSON progress and reports via IProgress.
    /// </summary>
    public partial async Task<CommandResponse<Unit>> PushAsync(
        DriverContext context, string image,
        IProgress<ImagePushProgress> progress,
        CancellationToken cancellationToken)
    {
      var path = $"/images/{Uri.EscapeDataString(image)}/push";

      Stream stream;
      try
      {
        stream = await Connection.PostStreamAsync(path, null, cancellationToken);
      }
      catch (HttpRequestException ex)
      {
        return CommandResponse<Unit>.Fail(
            $"Push failed: {ex.Message}",
            ErrorCodes.Image.PushFailed,
            CreateErrorContext("POST /images/push", 0));
      }

      string lastError = null;
      using var reader = new StreamReader(stream, Encoding.UTF8);
      while (!cancellationToken.IsCancellationRequested)
      {
        string line;
        try
        { line = await reader.ReadLineAsync(cancellationToken); }
        catch { break; }
        if (line == null)
          break;
        if (string.IsNullOrWhiteSpace(line))
          continue;

        var parsed = JsonConvert.DeserializeObject<PushProgressLine>(line);
        if (parsed == null)
          continue;

        if (!string.IsNullOrWhiteSpace(parsed.Error))
        {
          lastError = parsed.Error;
          break;
        }

        progress?.Report(new ImagePushProgress
        {
          Status = parsed.Status,
          Progress = parsed.Progress,
          Id = parsed.Id,
          Current = parsed.ProgressDetail?.Current ?? 0,
          Total = parsed.ProgressDetail?.Total ?? 0
        });
      }

      if (!string.IsNullOrWhiteSpace(lastError))
        return CommandResponse<Unit>.Fail(lastError,
            ErrorCodes.Image.PushFailed,
            CreateErrorContext("POST /images/push", 0));

      return CommandResponse<Unit>.Ok(Unit.Default);
    }

    #endregion

    #region Build

    /// <summary>
    /// Builds an image from a Dockerfile via POST /build.
    /// Creates a tar archive from the build context directory using SharpCompress,
    /// streams NDJSON build output, and extracts the image ID from the aux message.
    /// </summary>
    public partial async Task<CommandResponse<ImageBuildResult>> BuildAsync(
        DriverContext context, ImageBuildConfig config,
        IProgress<ImageBuildProgress> progress,
        CancellationToken cancellationToken)
    {
      if (string.IsNullOrEmpty(config?.BuildContext))
        return CommandResponse<ImageBuildResult>.Fail(
            "BuildContext is required",
            ErrorCodes.Config.Missing);

      if (!Directory.Exists(config.BuildContext))
        return CommandResponse<ImageBuildResult>.Fail(
            $"Build context directory not found: {config.BuildContext}",
            ErrorCodes.Image.BuildFailed);

      // Create tar archive of the build context
      var tarStream = CreateBuildContextTar(config.BuildContext);

      var query = BuildBuildQueryParams(config);
      var path = "/build" + (query.Count > 0 ? "?" + string.Join("&", query) : "");

      var content = new StreamContent(tarStream);
      content.Headers.ContentType =
          new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-tar");

      var buildResult = new ImageBuildResult();
      string lastError = null;

      await foreach (var line in ReadNdjsonFromPostStreamAsync(
          path, content, cancellationToken))
      {
        var parsed = JsonConvert.DeserializeObject<BuildOutputLine>(line);
        if (parsed == null)
          continue;

        // Extract image ID from aux message
        if (parsed.Aux?.Id != null)
        {
          buildResult.ImageId = parsed.Aux.Id;
        }

        // Collect build output
        if (!string.IsNullOrEmpty(parsed.Stream))
        {
          buildResult.Output.Add(parsed.Stream.TrimEnd('\n'));
        }

        // Check for errors
        if (!string.IsNullOrWhiteSpace(parsed.Error))
        {
          lastError = parsed.ErrorDetail?.Message ?? parsed.Error;
          break;
        }

        progress?.Report(new ImageBuildProgress
        {
          Stream = parsed.Stream,
          Status = parsed.Aux != null ? $"Built: {parsed.Aux.Id}" : null,
          Id = parsed.Aux?.Id,
          Error = parsed.Error
        });
      }

      // Dispose the tar stream after build completes
      await tarStream.DisposeAsync();

      if (!string.IsNullOrWhiteSpace(lastError))
        return CommandResponse<ImageBuildResult>.Fail(lastError,
            ErrorCodes.Image.BuildFailed,
            CreateErrorContext("POST /build", 0));

      return CommandResponse<ImageBuildResult>.Ok(buildResult);
    }

    #endregion

    #region Build Helpers

    private static MemoryStream CreateBuildContextTar(string contextPath)
    {
      var memoryStream = new MemoryStream();
      using (var writer = WriterFactory.Open(
          memoryStream, ArchiveType.Tar, new TarWriterOptions(CompressionType.None, true)))
      {
        var contextDir = new DirectoryInfo(contextPath);
        var files = contextDir.GetFiles("*", SearchOption.AllDirectories);

        foreach (var file in files)
        {
          var relativePath = Path.GetRelativePath(contextPath, file.FullName)
              .Replace('\\', '/');
          using var fileStream = file.OpenRead();
          writer.Write(relativePath, fileStream, file.LastWriteTimeUtc);
        }
      }

      memoryStream.Position = 0;
      return memoryStream;
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
        var json = JsonConvert.SerializeObject(config.BuildArgs);
        query.Add($"buildargs={Uri.EscapeDataString(json)}");
      }

      if (config.Labels?.Count > 0)
      {
        var json = JsonConvert.SerializeObject(config.Labels);
        query.Add($"labels={Uri.EscapeDataString(json)}");
      }

      return query;
    }

    #endregion
  }
}
