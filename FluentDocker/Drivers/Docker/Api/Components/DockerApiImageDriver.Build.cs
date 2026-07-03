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
      // A digest reference (repo@sha256:...) must be passed through on fromImage with no
      // tag param. Forcing tag=latest or appending a separate &tag= would fight the digest.
      var fromImage = image;
      var digestInTag = tag != null &&
          tag.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase);
      if (digestInTag && image != null && !image.Contains('@'))
        fromImage = $"{image}@{tag}";

      var isDigestRef = (fromImage != null && fromImage.Contains('@')) || digestInTag;

      // Reject a request carrying two different digests (one in the ref, one in the tag) instead
      // of silently honoring the ref and discarding the tag.
      if (image != null && image.Contains("@sha256:", StringComparison.OrdinalIgnoreCase) && digestInTag)
      {
        var refDigest = image[(image.IndexOf('@') + 1)..];
        if (!string.Equals(refDigest, tag, StringComparison.OrdinalIgnoreCase))
          return CommandResponse<Unit>.Fail(
              $"Conflicting digests in image '{image}' and tag '{tag}'",
              ErrorCodes.Image.PullFailed,
              CreateErrorContext("POST /images/create (pull)", 0));
      }

      string path;
      string displayRef;
      if (isDigestRef)
      {
        path = $"/images/create?fromImage={Uri.EscapeDataString(fromImage)}";
        displayRef = fromImage;
      }
      else
      {
        tag ??= "latest";
        path = $"/images/create" +
               $"?fromImage={Uri.EscapeDataString(image)}" +
               $"&tag={Uri.EscapeDataString(tag)}";
        displayRef = $"{image}:{tag}";
      }

      // Use PostStreamAsync directly so we can detect connection failures
      Stream stream;
      try
      {
        stream = await Connection.PostStreamAsync(
            path, null, DockerApiRegistryAuth.HeaderFor(Connection, image), cancellationToken)
            .ConfigureAwait(false);
      }
      catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
      {
        throw;
      }
      catch (OperationCanceledException ex)
      {
        return CommandResponse<Unit>.Fail(
            $"Cannot connect to Docker daemon: {ex.Message}",
            ErrorCodes.Image.PullFailed,
            CreateErrorContext("POST /images/create (pull)", 0));
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

      // ResponseOwningStream owns the underlying HttpResponseMessage; dispose it so the
      // connection is returned to the pool instead of leaking once the stream is drained.
      try
      {
        await using (stream.ConfigureAwait(false))
        {
          await foreach (var parsed in ReadNdjsonLinesAsync(
              stream, DockerApiJsonContext.Default.PullProgressLine, cancellationToken))
          {
            receivedProgress = true;

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
        }
      }
      catch (DriverException ex)
      {
        // The NDJSON reader throws DriverException on stream read failure.
        return CommandResponse<Unit>.Fail(ex.Message,
            ErrorCodes.Image.PullFailed,
            CreateErrorContext("POST /images/create (pull)", 0));
      }
      catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
      {
        throw;
      }
      catch (OperationCanceledException ex)
      {
        return CommandResponse<Unit>.Fail(
            $"Cannot connect to Docker daemon: {ex.Message}",
            ErrorCodes.Image.PullFailed,
            CreateErrorContext("POST /images/create (pull)", 0));
      }

      if (!string.IsNullOrWhiteSpace(lastError))
        return CommandResponse<Unit>.Fail(lastError,
            ErrorCodes.Image.PullFailed,
            CreateErrorContext("POST /images/create (pull)", 0));

      if (!receivedProgress)
        return CommandResponse<Unit>.Fail(
            $"Pull received no response from Docker daemon for '{displayRef}'",
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
        stream = await Connection.PostStreamAsync(
            path, null, DockerApiRegistryAuth.HeaderFor(Connection, image), cancellationToken)
            .ConfigureAwait(false);
      }
      catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
      {
        throw;
      }
      catch (OperationCanceledException ex)
      {
        return CommandResponse<Unit>.Fail(
            $"Cannot connect to Docker daemon: {ex.Message}",
            ErrorCodes.Image.PushFailed,
            CreateErrorContext("POST /images/{name}/push", 0));
      }
      catch (HttpRequestException ex)
      {
        return CommandResponse<Unit>.Fail(
            $"Push failed: {ex.Message}",
            ErrorCodes.Image.PushFailed,
            CreateErrorContext("POST /images/push", 0));
      }

      string lastError = null;
      var receivedProgress = false;

      // ResponseOwningStream owns the HttpResponseMessage; dispose it after draining.
      try
      {
        await using (stream.ConfigureAwait(false))
        {
          await foreach (var parsed in ReadNdjsonLinesAsync(
              stream, DockerApiJsonContext.Default.PushProgressLine, cancellationToken))
          {
            receivedProgress = true;

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
        }
      }
      catch (DriverException ex)
      {
        // The NDJSON reader throws DriverException on stream read failure.
        return CommandResponse<Unit>.Fail(ex.Message,
            ErrorCodes.Image.PushFailed,
            CreateErrorContext("POST /images/{name}/push", 0));
      }
      catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
      {
        throw;
      }
      catch (OperationCanceledException ex)
      {
        return CommandResponse<Unit>.Fail(
            $"Cannot connect to Docker daemon: {ex.Message}",
            ErrorCodes.Image.PushFailed,
            CreateErrorContext("POST /images/{name}/push", 0));
      }

      if (!string.IsNullOrWhiteSpace(lastError))
        return CommandResponse<Unit>.Fail(lastError,
            ErrorCodes.Image.PushFailed,
            CreateErrorContext("POST /images/push", 0));

      // A successful push always emits at least one progress/status line. None means the stream
      // ended without evidence of success (incomplete/streamed failure).
      if (!receivedProgress)
        return CommandResponse<Unit>.Fail(
            $"Push received no response from Docker daemon for '{image}'",
            ErrorCodes.Image.PushFailed,
            CreateErrorContext("POST /images/{name}/push", 0));

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

      Stream tarStream;
      try
      {
        tarStream = CreateBuildContextTar(config.BuildContext, config);
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
        return CommandResponse<ImageBuildResult>.Fail(
            $"Failed to create build context tar: {ex.Message}",
            ErrorCodes.Image.BuildFailed,
            CreateErrorContext("POST /build", 0));
      }

      try
      {
        var query = BuildBuildQueryParams(config);
        var path = "/build" + (query.Count > 0 ? "?" + string.Join("&", query) : "");

        var content = new StreamContent(tarStream);
        content.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-tar");

        var buildResult = new ImageBuildResult();
        string lastError = null;

        var headers = DockerApiRegistryAuth.RegistryConfigHeaderFor(Connection);
        await foreach (var parsed in ReadNdjsonFromPostStreamAsync(
            path, content, headers, DockerApiJsonContext.Default.BuildOutputLine, cancellationToken))
        {
          if (parsed.Aux?.Id != null)
            buildResult.ImageId = parsed.Aux.Id;

          if (!string.IsNullOrEmpty(parsed.Stream))
            buildResult.Output.Add(parsed.Stream.TrimEnd('\n'));

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

        return CommandResponse<ImageBuildResult>.Ok(buildResult);
      }
      catch (DriverException ex)
      {
        // The NDJSON reader throws DriverException on stream open/read failure.
        return CommandResponse<ImageBuildResult>.Fail(ex.Message,
            ErrorCodes.Image.BuildFailed,
            CreateErrorContext("POST /build", 0));
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
        return CommandResponse<ImageBuildResult>.Fail(ex.Message,
            ErrorCodes.Image.BuildFailed,
            CreateErrorContext("POST /build", 0));
      }
      finally
      {
        await tarStream.DisposeAsync().ConfigureAwait(false);
      }
    }

    #endregion
  }
}
