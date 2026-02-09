using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers.Docker.Api.Connection;
using FluentDocker.Model.Drivers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Image = FluentDocker.Drivers.Image;

namespace FluentDocker.Drivers.Docker.Api.Components
{
  /// <summary>
  /// Docker API implementation of IImageDriver.
  /// Streaming operations (pull, push, build) are in the Build partial file.
  /// </summary>
  public partial class DockerApiImageDriver : DockerApiDriverBase, IImageDriver
  {
    public DockerApiImageDriver(IDockerApiConnection connection) : base(connection) { }

    #region List/Inspect Operations

    /// <summary>Lists images via GET /images/json.</summary>
    public async Task<CommandResponse<IList<Image>>> ListAsync(
        DriverContext context, ImageListFilter filter = null,
        CancellationToken cancellationToken = default)
    {
      var query = new List<string>();
      if (filter != null)
      {
        if (filter.All)
          query.Add("all=true");
        var filters = BuildListFilters(filter);
        if (filters.Count > 0)
        {
          var json = JsonConvert.SerializeObject(filters);
          query.Add($"filters={Uri.EscapeDataString(json)}");
        }
      }

      var path = "/images/json";
      if (query.Count > 0)
        path += "?" + string.Join("&", query);

      var result = await GetJsonAsync<JArray>(path, cancellationToken);
      if (!result.Success)
        return CommandResponse<IList<Image>>.Fail(result.ErrorMessage,
            MapHttpErrorCode(result.StatusCode),
            CreateErrorContext("GET /images/json", result.StatusCode, result.ResponseBody),
            result.StatusCode);

      var images = result.Data?.Select(ParseImage).ToList() ?? new List<Image>();
      return CommandResponse<IList<Image>>.Ok(images);
    }

    /// <summary>Inspects an image via GET /images/{name}/json.</summary>
    public async Task<CommandResponse<Image>> InspectAsync(
        DriverContext context, string imageId,
        CancellationToken cancellationToken = default)
    {
      var path = $"/images/{Uri.EscapeDataString(imageId)}/json";
      var result = await GetJsonAsync<JObject>(path, cancellationToken);
      if (!result.Success)
        return CommandResponse<Image>.Fail(result.ErrorMessage,
            MapNotFoundErrorCode(result.StatusCode, ErrorCodes.Image.NotFound),
            CreateErrorContext($"GET {path}", result.StatusCode, result.ResponseBody),
            result.StatusCode);

      return CommandResponse<Image>.Ok(ParseInspectImage(result.Data));
    }

    /// <summary>Gets image history via GET /images/{name}/history.</summary>
    public async Task<CommandResponse<IList<ImageLayer>>> HistoryAsync(
        DriverContext context, string imageId,
        CancellationToken cancellationToken = default)
    {
      var path = $"/images/{Uri.EscapeDataString(imageId)}/history";
      var result = await GetJsonAsync<JArray>(path, cancellationToken);
      if (!result.Success)
        return CommandResponse<IList<ImageLayer>>.Fail(result.ErrorMessage,
            MapNotFoundErrorCode(result.StatusCode, ErrorCodes.Image.HistoryFailed),
            CreateErrorContext($"GET {path}", result.StatusCode, result.ResponseBody),
            result.StatusCode);

      var layers = result.Data?.Select(ParseImageLayer).ToList() ?? new List<ImageLayer>();
      return CommandResponse<IList<ImageLayer>>.Ok(layers);
    }

    #endregion

    #region Tag/Remove Operations

    /// <summary>Tags an image via POST /images/{name}/tag.</summary>
    public async Task<CommandResponse<Unit>> TagAsync(
        DriverContext context, string imageId, string repository, string tag,
        CancellationToken cancellationToken = default)
    {
      var path = $"/images/{Uri.EscapeDataString(imageId)}/tag" +
                 $"?repo={Uri.EscapeDataString(repository)}" +
                 $"&tag={Uri.EscapeDataString(tag)}";

      var result = await PostAsync(path, null, cancellationToken);
      if (!result.Success)
        return CommandResponse<Unit>.Fail(result.ErrorMessage,
            MapNotFoundErrorCode(result.StatusCode, ErrorCodes.Image.TagFailed),
            CreateErrorContext($"POST /images/{imageId}/tag", result.StatusCode, result.ResponseBody),
            result.StatusCode);

      return CommandResponse<Unit>.Ok(Unit.Default);
    }

    /// <summary>Removes an image via DELETE /images/{name}.</summary>
    public async Task<CommandResponse<ImageRemoveResult>> RemoveAsync(
        DriverContext context, string imageId, bool force = false, bool noPrune = false,
        CancellationToken cancellationToken = default)
    {
      var path = $"/images/{Uri.EscapeDataString(imageId)}" +
                 $"?force={force.ToString().ToLower()}" +
                 $"&noprune={noPrune.ToString().ToLower()}";

      // DELETE /images returns a JSON array; use Connection directly
      // since base DeleteAsync returns ApiResult without body parsing.
      HttpResponseMessage response;
      try
      {
        response = await Connection.DeleteAsync(path, cancellationToken);
      }
      catch (Exception ex) when (ex is HttpRequestException
          or System.Net.Sockets.SocketException or TaskCanceledException)
      {
        return CommandResponse<ImageRemoveResult>.Fail(
            $"Cannot connect to Docker daemon: {ex.Message}",
            ErrorCodes.Api.ConnectionFailed,
            CreateErrorContext($"DELETE /images/{imageId}", (int)HttpStatusCode.ServiceUnavailable),
            (int)HttpStatusCode.ServiceUnavailable);
      }

      var body = await response.Content.ReadAsStringAsync(cancellationToken);
      if (!response.IsSuccessStatusCode)
      {
        var errorMsg = TryExtractErrorMessage(body) ??
            $"Docker API returned {(int)response.StatusCode}: {response.ReasonPhrase}";
        return CommandResponse<ImageRemoveResult>.Fail(errorMsg,
            MapNotFoundErrorCode((int)response.StatusCode, ErrorCodes.Image.RemoveFailed),
            CreateErrorContext($"DELETE /images/{imageId}", (int)response.StatusCode, body),
            (int)response.StatusCode);
      }

      var removeResult = new ImageRemoveResult();
      if (!string.IsNullOrWhiteSpace(body))
      {
        var items = JsonConvert.DeserializeObject<JArray>(body);
        foreach (var item in items ?? new JArray())
        {
          var deleted = item.Value<string>("Deleted");
          var untagged = item.Value<string>("Untagged");
          if (!string.IsNullOrEmpty(deleted))
            removeResult.Deleted.Add(deleted);
          if (!string.IsNullOrEmpty(untagged))
            removeResult.Untagged.Add(untagged);
        }
      }

      return CommandResponse<ImageRemoveResult>.Ok(removeResult);
    }

    /// <summary>Prunes unused images via POST /images/prune.</summary>
    public async Task<CommandResponse<ImagePruneResult>> PruneAsync(
        DriverContext context, bool all = false, Dictionary<string, string> filter = null,
        CancellationToken cancellationToken = default)
    {
      var filters = new Dictionary<string, List<string>>();
      if (all)
        filters["dangling"] = new List<string> { "false" };
      if (filter != null)
      {
        foreach (var kv in filter)
          filters[kv.Key] = new List<string> { kv.Value };
      }

      var path = "/images/prune";
      if (filters.Count > 0)
      {
        var json = JsonConvert.SerializeObject(filters);
        path += $"?filters={Uri.EscapeDataString(json)}";
      }

      var result = await PostJsonAsync<JObject>(path, null, cancellationToken);
      if (!result.Success)
        return CommandResponse<ImagePruneResult>.Fail(result.ErrorMessage,
            ErrorCodes.Image.PruneFailed,
            CreateErrorContext("POST /images/prune", result.StatusCode, result.ResponseBody),
            result.StatusCode);

      var pruneResult = new ImagePruneResult
      {
        SpaceReclaimed = result.Data?.Value<long?>("SpaceReclaimed") ?? 0
      };
      if (result.Data?["ImagesDeleted"] is JArray deleted)
      {
        foreach (var item in deleted)
        {
          var d = item.Value<string>("Deleted");
          var u = item.Value<string>("Untagged");
          if (!string.IsNullOrEmpty(d))
            pruneResult.ImagesDeleted.Add(d);
          else if (!string.IsNullOrEmpty(u))
            pruneResult.ImagesDeleted.Add(u);
        }
      }

      return CommandResponse<ImagePruneResult>.Ok(pruneResult);
    }

    #endregion

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
      { stream = await GetRawStreamAsync(path, cancellationToken); }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail($"Failed to save images: {ex.Message}",
            ErrorCodes.Image.SaveFailed, CreateErrorContext("GET /images/get", 0));
      }

      try
      {
        await using (stream)
        await using (var fileStream = File.Create(outputPath))
          await stream.CopyToAsync(fileStream, cancellationToken);
        return CommandResponse<Unit>.Ok(Unit.Default);
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail($"Failed to write tar archive: {ex.Message}",
            ErrorCodes.Image.SaveFailed, CreateErrorContext("GET /images/get", 0));
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
      await foreach (var line in ReadNdjsonFromPostStreamAsync(
          "/images/load", content, cancellationToken))
      {
        var json = JObject.Parse(line);
        var streamVal = json.Value<string>("stream");
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

        var error = json.Value<string>("error");
        if (!string.IsNullOrWhiteSpace(error))
          return CommandResponse<IList<string>>.Fail(error,
              ErrorCodes.Image.LoadFailed, CreateErrorContext("POST /images/load", 0));
      }

      return CommandResponse<IList<string>>.Ok(loadedImages);
    }

    /// <summary>Imports a container filesystem as an image via POST /images/create.</summary>
    public async Task<CommandResponse<string>> ImportAsync(
        DriverContext context, string source, string repository = null,
        string tag = null, string message = null,
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
      await foreach (var line in ReadNdjsonFromPostStreamAsync(
          path, content, cancellationToken))
      {
        var json = JObject.Parse(line);
        var status = json.Value<string>("status");
        if (!string.IsNullOrWhiteSpace(status))
          importedId = status.Trim();

        var error = json.Value<string>("error");
        if (!string.IsNullOrWhiteSpace(error))
          return CommandResponse<string>.Fail(error,
              ErrorCodes.Image.ImportFailed, CreateErrorContext("POST /images/create", 0));
      }

      return CommandResponse<string>.Ok(importedId ?? string.Empty);
    }

    #endregion

    #region Partial Method Declarations (implemented in Build partial)

    public partial Task<CommandResponse<Unit>> PullAsync(
        DriverContext context, string image, string tag,
        IProgress<ImagePullProgress> progress, CancellationToken cancellationToken);

    public partial Task<CommandResponse<Unit>> PushAsync(
        DriverContext context, string image,
        IProgress<ImagePushProgress> progress, CancellationToken cancellationToken);

    public partial Task<CommandResponse<ImageBuildResult>> BuildAsync(
        DriverContext context, ImageBuildConfig config,
        IProgress<ImageBuildProgress> progress, CancellationToken cancellationToken);

    #endregion

    #region JSON Parsing Helpers

    private static Image ParseImage(JToken token)
    {
      if (token == null)
        return new Image();
      return new Image
      {
        Id = token.Value<string>("Id"),
        ParentId = token.Value<string>("ParentId") ?? string.Empty,
        RepoTags = token["RepoTags"]?.ToObject<List<string>>() ?? new List<string>(),
        RepoDigests = token["RepoDigests"]?.ToObject<List<string>>() ?? new List<string>(),
        Created = DateTimeOffset.FromUnixTimeSeconds(
              token.Value<long?>("Created") ?? 0).UtcDateTime,
        Size = token.Value<long?>("Size") ?? 0,
        VirtualSize = token.Value<long?>("VirtualSize") ?? 0,
        Labels = token["Labels"]?.ToObject<Dictionary<string, string>>()
              ?? new Dictionary<string, string>(),
        Containers = token.Value<int?>("Containers") ?? -1
      };
    }

    private static Image ParseInspectImage(JObject token)
    {
      if (token == null)
        return new Image();
      return new Image
      {
        Id = token.Value<string>("Id"),
        ParentId = token.Value<string>("Parent") ?? string.Empty,
        RepoTags = token["RepoTags"]?.ToObject<List<string>>() ?? new List<string>(),
        RepoDigests = token["RepoDigests"]?.ToObject<List<string>>() ?? new List<string>(),
        Created = token.Value<DateTime?>("Created") ?? DateTime.MinValue,
        Size = token.Value<long?>("Size") ?? 0,
        VirtualSize = token.Value<long?>("VirtualSize") ?? 0,
        Architecture = token.Value<string>("Architecture"),
        Os = token.Value<string>("Os"),
        Labels = ExtractLabels(token),
        Containers = -1
      };
    }

    private static Dictionary<string, string> ExtractLabels(JObject token)
    {
      var config = token["Config"] as JObject;
      return config?["Labels"]?.ToObject<Dictionary<string, string>>()
          ?? new Dictionary<string, string>();
    }

    private static ImageLayer ParseImageLayer(JToken token)
    {
      if (token == null)
        return new ImageLayer();
      return new ImageLayer
      {
        Id = token.Value<string>("Id") ?? string.Empty,
        CreatedBy = token.Value<string>("CreatedBy") ?? string.Empty,
        Created = DateTimeOffset.FromUnixTimeSeconds(
              token.Value<long?>("Created") ?? 0).UtcDateTime,
        Size = token.Value<long?>("Size") ?? 0,
        Comment = token.Value<string>("Comment") ?? string.Empty,
        Tags = token["Tags"]?.ToObject<List<string>>() ?? new List<string>()
      };
    }

    private static Dictionary<string, List<string>> BuildListFilters(ImageListFilter filter)
    {
      var filters = new Dictionary<string, List<string>>();
      if (filter.Dangling.HasValue)
        filters["dangling"] = new List<string> { filter.Dangling.Value.ToString().ToLower() };
      if (!string.IsNullOrEmpty(filter.Reference))
        filters["reference"] = new List<string> { filter.Reference };
      if (!string.IsNullOrEmpty(filter.Before))
        filters["before"] = new List<string> { filter.Before };
      if (!string.IsNullOrEmpty(filter.Since))
        filters["since"] = new List<string> { filter.Since };
      if (filter.Labels?.Count > 0)
      {
        filters["label"] = filter.Labels
            .Select(kv => string.IsNullOrEmpty(kv.Value) ? kv.Key : $"{kv.Key}={kv.Value}")
            .ToList();
      }
      return filters;
    }

    private static string TryExtractErrorMessage(string body)
    {
      if (string.IsNullOrWhiteSpace(body))
        return null;
      try
      { return JObject.Parse(body).Value<string>("message"); }
      catch { return body.Length > 500 ? body[..500] : body; }
    }

    #endregion
  }
}
