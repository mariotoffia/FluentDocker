#nullable disable warnings
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Drivers.Docker.Api.Components
{
  /// <summary>
  /// Partial class extending DockerApiImageDriver with local archive transfer operations:
  /// save, load, and import. The atomic file-write helper lives in the SaveAtomic partial.
  /// </summary>
  public partial class DockerApiImageDriver
  {
    #region Save/Load/Import Operations

    /// <summary>Saves images to a tar archive via GET /images/get.</summary>
    public async Task<CommandResponse<Unit>> SaveAsync(
        DriverContext context, string[] images, string outputPath,
        CancellationToken cancellationToken = default)
    {
      var names = string.Join("&", images.Select(i => $"names={Uri.EscapeDataString(i)}"));
      var path = $"/images/get?{names}";

      Stream stream;
      try
      { stream = await GetRawStreamAsync(path, cancellationToken).ConfigureAwait(false); }
      catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
      {
        throw;
      }
      catch (Exception ex)
      {
        var statusCode = ex is HttpRequestException { StatusCode: not null } httpEx
            ? (int)httpEx.StatusCode.Value
            : 0;
        return CommandResponse<Unit>.Fail($"Failed to save images: {ex.Message}",
            ErrorCodes.Image.SaveFailed, CreateErrorContext("GET /images/get", statusCode),
            statusCode);
      }

      try
      {
        await using (stream)
        {
          await WriteStreamAtomicallyAsync(stream, outputPath, cancellationToken).ConfigureAwait(false);
        }
        return CommandResponse<Unit>.Ok(Unit.Default);
      }
      catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail($"Failed to write tar archive: {ex.Message}",
            ErrorCodes.Image.SaveFailed, CreateErrorContext("GET /images/get", HttpStatusCodeOrZero(ex)));
      }
    }

    /// <summary>Loads images from a tar archive via POST /images/load.</summary>
    public async Task<CommandResponse<IList<string>>> LoadAsync(
        DriverContext context, string inputPath,
        CancellationToken cancellationToken = default)
    {
      if (!File.Exists(inputPath))
        return CommandResponse<IList<string>>.Fail(
            $"File not found: {inputPath}", ErrorCodes.Image.LoadFailed);

      await using var fileStream = File.OpenRead(inputPath);
      var content = new StreamContent(fileStream);
      content.Headers.ContentType =
          new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-tar");

      var loadedImages = new List<string>();
      try
      {
        await foreach (var line in ReadNdjsonFromPostStreamAsync(
            "/images/load", content, cancellationToken).ConfigureAwait(false))
        {
          var json = JsonHelper.ParseElement(line);
          var streamVal = json.GetStringOrDefault("stream");
          if (!string.IsNullOrWhiteSpace(streamVal))
          {
            var trimmed = streamVal.Trim();
            if (trimmed.StartsWith("Loaded image:", StringComparison.OrdinalIgnoreCase))
            {
              var name = trimmed["Loaded image:".Length..].Trim();
              if (!string.IsNullOrEmpty(name))
                loadedImages.Add(name);
            }
            else if (trimmed.StartsWith("Loaded image ID:", StringComparison.OrdinalIgnoreCase))
            {
              var id = trimmed["Loaded image ID:".Length..].Trim();
              if (!string.IsNullOrEmpty(id))
                loadedImages.Add(id);
            }
          }

          var error = json.GetStringOrDefault("error");
          if (!string.IsNullOrWhiteSpace(error))
            return CommandResponse<IList<string>>.Fail(error,
                ErrorCodes.Image.LoadFailed, CreateErrorContext("POST /images/load", 0));
        }
      }
      catch (DriverException ex)
      {
        return CommandResponse<IList<string>>.Fail(ex.Message,
            ErrorCodes.Image.LoadFailed, CreateErrorContext("POST /images/load", HttpStatusCodeOrZero(ex)));
      }
      catch (JsonException ex)
      {
        // A daemon crash mid-stream can truncate the final NDJSON line; surface it as a
        // typed failure instead of letting a raw JsonException escape the CommandResponse
        // contract.
        return CommandResponse<IList<string>>.Fail(
            $"Malformed NDJSON line in docker load stream (truncated daemon response?): {ex.Message}",
            ErrorCodes.Image.LoadFailed, CreateErrorContext("POST /images/load", 0));
      }

      // A successful load emits at least one "Loaded image[: | ID:]" line. None means the
      // stream completed without evidence of success (incomplete/streamed failure).
      if (loadedImages.Count == 0)
        return CommandResponse<IList<string>>.Fail(
            "Docker load returned no loaded images (incomplete/streamed failure)",
            ErrorCodes.Image.LoadFailed, CreateErrorContext("POST /images/load", 0));

      return CommandResponse<IList<string>>.Ok(loadedImages);
    }

    /// <summary>Imports a container filesystem as an image via POST /images/create.</summary>
    public async Task<CommandResponse<string>> ImportAsync(
        DriverContext context, string source, string? repository = null,
        string? tag = null, string? message = null,
        CancellationToken cancellationToken = default)
    {
      var query = new List<string> { "fromSrc=-" };
      if (!string.IsNullOrEmpty(repository))
        query.Add($"repo={Uri.EscapeDataString(repository)}");
      if (!string.IsNullOrEmpty(tag))
        query.Add($"tag={Uri.EscapeDataString(tag)}");
      if (!string.IsNullOrEmpty(message))
        query.Add($"message={Uri.EscapeDataString(message)}");

      var path = "/images/create?" + string.Join("&", query);

      if (!File.Exists(source))
        return CommandResponse<string>.Fail(
            $"Source file not found: {source}", ErrorCodes.Image.ImportFailed);

      await using var fileStream = File.OpenRead(source);
      var content = new StreamContent(fileStream);
      content.Headers.ContentType =
          new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-tar");

      string importedId = null;
      try
      {
        await foreach (var line in ReadNdjsonFromPostStreamAsync(
            path, content, cancellationToken).ConfigureAwait(false))
        {
          var json = JsonHelper.ParseElement(line);
          var status = json.GetStringOrDefault("status");
          if (!string.IsNullOrWhiteSpace(status))
            importedId = status.Trim();

          var error = json.GetStringOrDefault("error");
          if (!string.IsNullOrWhiteSpace(error))
            return CommandResponse<string>.Fail(error,
                ErrorCodes.Image.ImportFailed, CreateErrorContext("POST /images/create", 0));
        }
      }
      catch (DriverException ex)
      {
        return CommandResponse<string>.Fail(ex.Message,
            ErrorCodes.Image.ImportFailed, CreateErrorContext("POST /images/create", HttpStatusCodeOrZero(ex)));
      }
      catch (JsonException ex)
      {
        // A daemon crash mid-stream can truncate the final NDJSON line; surface it as a
        // typed failure instead of letting a raw JsonException escape the CommandResponse
        // contract.
        return CommandResponse<string>.Fail(
            $"Malformed NDJSON line in docker import stream (truncated daemon response?): {ex.Message}",
            ErrorCodes.Image.ImportFailed, CreateErrorContext("POST /images/create", 0));
      }

      // A successful import emits a status line carrying the new image id.
      if (string.IsNullOrEmpty(importedId))
        return CommandResponse<string>.Fail(
            "Docker import returned no image id",
            ErrorCodes.Image.ImportFailed, CreateErrorContext("POST /images/create", 0));

      return CommandResponse<string>.Ok(importedId);
    }

    #endregion
  }
}
