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
  /// Partial class extending DockerApiImageDriver with streaming registry-transfer operations:
  /// pull and push.
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
      if (string.IsNullOrWhiteSpace(image))
        return CommandResponse<Unit>.Fail(
            "Image is required",
            ErrorCodes.General.InvalidArgument,
            CreateErrorContext("POST /images/create (pull)", 0));

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
        // An embedded tag in `image` (e.g. "nginx:1.25") must be split into fromImage/tag: the
        // daemon's reference.WithTag rebuilds the ref from the repository domain/path only and
        // silently discards a tag baked into `image`, pulling ":latest" instead. Mirrors the CLI
        // driver's ShouldAppendTag embedded-tag detection so both drivers pull the same reference.
        if (TrySplitEmbeddedTag(image, out var repo, out var embeddedTag))
        {
          if (tag != null && !string.Equals(tag, embeddedTag, StringComparison.OrdinalIgnoreCase))
            return CommandResponse<Unit>.Fail(
                $"Conflicting tags in image '{image}' and tag '{tag}'",
                ErrorCodes.General.InvalidArgument,
                CreateErrorContext("POST /images/create (pull)", 0));

          image = repo;
          tag = embeddedTag;
        }

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
            CreateErrorContext("POST /images/create (pull)", HttpStatusCodeOrZero(ex)));
      }
      catch (HttpRequestException ex)
      {
        var statusCode = HttpStatusCodeOrZero(ex);
        return CommandResponse<Unit>.Fail(
            $"Pull failed: {ex.Message}",
            ErrorCodes.Image.PullFailed,
            CreateErrorContext("POST /images/create (pull)", statusCode),
            statusCode);
      }
      catch (DriverException ex)
      {
        // The unsupported-daemon-version negotiation failure is deliberately rethrown by the
        // connection so the request helpers map it to CommandResponse.Fail; map it here via
        // DescribeTransportFailure (505 -> Api.UnsupportedVersion) instead of letting it escape
        // raw through the CommandResponse contract (DAPI-2).
        var (statusCode, message) = DescribeTransportFailure(ex);
        return CommandResponse<Unit>.Fail(
            message,
            MapHttpErrorCode(statusCode),
            CreateErrorContext("POST /images/create (pull)", statusCode),
            statusCode);
      }

      string lastError = null;
      var receivedProgress = false;
      var receivedTerminalStatus = false;

      // ResponseOwningStream owns the underlying HttpResponseMessage; dispose it so the
      // connection is returned to the pool instead of leaking once the stream is drained.
      try
      {
        await using (stream.ConfigureAwait(false))
        {
          await foreach (var parsed in ReadNdjsonLinesAsync(
              stream, DockerApiJsonContext.Default.PullProgressLine, cancellationToken)
              .ConfigureAwait(false))
          {
            receivedProgress = true;
            if (IsTerminalPullStatus(parsed.Status))
              receivedTerminalStatus = true;

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
            CreateErrorContext("POST /images/create (pull)", HttpStatusCodeOrZero(ex)));
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
            CreateErrorContext("POST /images/create (pull)", HttpStatusCodeOrZero(ex)));
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

      // ponytail: existence probe can false-positive on a stale local tag after an interrupted
      // pull; a digest check would close that gap if this becomes observable in production.
      if (!receivedTerminalStatus &&
          !await ImageExistsAsync(displayRef, cancellationToken).ConfigureAwait(false))
        return CommandResponse<Unit>.Fail(
            $"Docker pull for '{displayRef}' ended without terminal success and the image is not present",
            ErrorCodes.Image.PullFailed,
            CreateErrorContext("GET /images/{name}/json", 0));

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
      if (string.IsNullOrWhiteSpace(image))
        return CommandResponse<Unit>.Fail(
            "Image is required",
            ErrorCodes.General.InvalidArgument,
            CreateErrorContext("POST /images/{name}/push", 0));

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
            CreateErrorContext("POST /images/{name}/push", HttpStatusCodeOrZero(ex)));
      }
      catch (HttpRequestException ex)
      {
        var statusCode = HttpStatusCodeOrZero(ex);
        return CommandResponse<Unit>.Fail(
            $"Push failed: {ex.Message}",
            ErrorCodes.Image.PushFailed,
            CreateErrorContext("POST /images/push", statusCode),
            statusCode);
      }
      catch (DriverException ex)
      {
        // The unsupported-daemon-version negotiation failure is deliberately rethrown by the
        // connection so the request helpers map it to CommandResponse.Fail; map it here via
        // DescribeTransportFailure (505 -> Api.UnsupportedVersion) instead of letting it escape
        // raw through the CommandResponse contract (DAPI-2).
        var (statusCode, message) = DescribeTransportFailure(ex);
        return CommandResponse<Unit>.Fail(
            message,
            MapHttpErrorCode(statusCode),
            CreateErrorContext("POST /images/{name}/push", statusCode),
            statusCode);
      }

      string lastError = null;
      var receivedProgress = false;
      var receivedTerminalStatus = false;

      // ResponseOwningStream owns the HttpResponseMessage; dispose it after draining.
      try
      {
        await using (stream.ConfigureAwait(false))
        {
          await foreach (var parsed in ReadNdjsonLinesAsync(
              stream, DockerApiJsonContext.Default.PushProgressLine, cancellationToken)
              .ConfigureAwait(false))
          {
            receivedProgress = true;
            if (IsTerminalPushStatus(parsed.Status) ||
                !string.IsNullOrWhiteSpace(parsed.Aux?.Digest))
              receivedTerminalStatus = true;

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
            CreateErrorContext("POST /images/{name}/push", HttpStatusCodeOrZero(ex)));
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
            CreateErrorContext("POST /images/{name}/push", HttpStatusCodeOrZero(ex)));
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

      if (!receivedTerminalStatus)
        return CommandResponse<Unit>.Fail(
            $"Docker push for '{image}' ended without terminal digest (possible truncated stream)",
            ErrorCodes.Image.PushFailed,
            CreateErrorContext("POST /images/{name}/push", 0));

      return CommandResponse<Unit>.Ok(Unit.Default);
    }

    private async Task<bool> ImageExistsAsync(string image, CancellationToken cancellationToken)
    {
      var path = $"/images/{Uri.EscapeDataString(image)}/json";
      var result = await GetJsonElementAsync(path, cancellationToken).ConfigureAwait(false);
      return result.Success;
    }

    /// <summary>
    /// Detects a tag embedded in the repository segment of an image reference (e.g.
    /// "nginx:1.25"), mirroring the CLI driver's <c>ShouldAppendTag</c> detection: a ':' after
    /// the last '/' is a tag; a ':' at or before the last '/' is a registry host:port (e.g.
    /// "registry:5000/nginx", not split). A digest reference ('@') never has an embedded tag —
    /// callers route those through the digest branch before reaching this helper.
    /// </summary>
    private static bool TrySplitEmbeddedTag(string image, out string repo, out string embeddedTag)
    {
      repo = image;
      embeddedTag = null;
      if (string.IsNullOrEmpty(image) || image.Contains('@', StringComparison.Ordinal))
        return false;

      var lastSlash = image.LastIndexOf('/');
      var lastSegment = lastSlash < 0 ? image : image[(lastSlash + 1)..];
      if (!lastSegment.Contains(':', StringComparison.Ordinal))
        return false;

      var colon = (lastSlash + 1) + lastSegment.LastIndexOf(':');
      repo = image[..colon];
      embeddedTag = image[(colon + 1)..];
      return true;
    }

    private static bool IsTerminalPullStatus(string status)
    {
      if (string.IsNullOrWhiteSpace(status))
        return false;
      var value = status.Trim();
      return value.StartsWith("Status:", StringComparison.OrdinalIgnoreCase) ||
          value.StartsWith("Downloaded newer image for ", StringComparison.OrdinalIgnoreCase) ||
          value.StartsWith("Image is up to date for ", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTerminalPushStatus(string status)
    {
      if (string.IsNullOrWhiteSpace(status))
        return false;
      var value = status.Trim();
      var digest = value.IndexOf("digest:", StringComparison.OrdinalIgnoreCase);
      return digest >= 0 &&
          value.IndexOf("sha256:", digest, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    #endregion
  }
}
