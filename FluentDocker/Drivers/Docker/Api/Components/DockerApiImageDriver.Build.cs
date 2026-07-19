#nullable disable warnings
using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers.Docker.Api.ApiModels;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Drivers.Docker.Api.Components
{
  /// <summary>
  /// Partial class extending DockerApiImageDriver with the streaming image build operation.
  /// Pull and push live in the Registry partial file.
  /// </summary>
  public partial class DockerApiImageDriver
  {
    #region Build

    /// <summary>
    /// Builds an image from a Dockerfile via POST /build.
    /// Creates a tar archive from the build context directory,
    /// streams NDJSON build output, and extracts the image ID from the aux message.
    /// </summary>
    /// <remarks>
    /// The Docker API driver uses the Engine's legacy builder endpoint. BuildKit-only
    /// Dockerfile features such as <c>RUN --mount</c> and heredocs require the CLI driver.
    /// </remarks>
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

      Stream tarStream;
      try
      {
        tarStream = await CreateBuildContextTarAsync(config.BuildContext, config, Logger, cancellationToken)
            .ConfigureAwait(false);
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
        return CommandResponse<ImageBuildResult>.Fail(
            $"Failed to create build context tar: {ex.Message}",
            ErrorCodes.Image.BuildFailed,
            CreateErrorContext("POST /build", HttpStatusCodeOrZero(ex)));
      }

      try
      {
        var query = BuildBuildQueryParams(config);
        var path = "/build" + (query.Count > 0 ? "?" + string.Join("&", query) : "");

        var content = new StreamContent(tarStream);
        content.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-tar");

        var buildResult = new ImageBuildResult();
        var buildOutput = CreateOutputTail();
        string lastError = null;

        var headers = DockerApiRegistryAuth.RegistryConfigHeaderFor(Connection);
        await foreach (var parsed in ReadNdjsonFromPostStreamAsync(
            path, content, headers, DockerApiJsonContext.Default.BuildOutputLine, cancellationToken)
            .ConfigureAwait(false))
        {
          if (parsed.Aux?.Id != null)
            buildResult.ImageId = parsed.Aux.Id;

          if (!string.IsNullOrEmpty(parsed.Stream))
            AppendOutputLine(buildOutput, parsed.Stream.TrimEnd('\n'));

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

        if (!string.IsNullOrWhiteSpace(lastError))
          return CommandResponse<ImageBuildResult>.Fail(lastError,
              ErrorCodes.Image.BuildFailed,
              CreateErrorContext("POST /build", 0));

        // A successful build always emits an aux.ID. If we reached the end of the stream
        // without an error AND without an image id, the build was incomplete or the stream
        // failed mid-flight — surface it as a failure rather than a false success.
        if (buildResult.ImageId == null)
          return CommandResponse<ImageBuildResult>.Fail(
              "Docker build produced no image id (incomplete/streamed failure)",
              ErrorCodes.Image.BuildFailed,
              CreateErrorContext("POST /build", 0));

        buildResult.Output = ToOutputLines(buildOutput);
        return CommandResponse<ImageBuildResult>.Ok(buildResult);
      }
      catch (DriverException ex)
      {
        // The NDJSON reader classifies stream open/read transport failures; preserve a
        // transient code (connection/timeout) so callers can retry, else it is a build error.
        var code = ErrorCodes.IsTransientCode(ex.ErrorCode) ? ex.ErrorCode : ErrorCodes.Image.BuildFailed;
        return CommandResponse<ImageBuildResult>.Fail(ex.Message, code,
            CreateErrorContext("POST /build", HttpStatusCodeOrZero(ex)));
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
        return CommandResponse<ImageBuildResult>.Fail(ex.Message,
            ErrorCodes.Image.BuildFailed,
            CreateErrorContext("POST /build", HttpStatusCodeOrZero(ex)));
      }
      finally
      {
        await tarStream.DisposeAsync().ConfigureAwait(false);
      }
    }

    #endregion
  }
}
