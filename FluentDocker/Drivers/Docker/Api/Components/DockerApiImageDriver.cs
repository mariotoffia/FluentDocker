using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers.Docker.Api.Connection;
using FluentDocker.Model.Drivers;
using Microsoft.Extensions.Logging;
using Image = FluentDocker.Drivers.Image;
namespace FluentDocker.Drivers.Docker.Api.Components
{
  /// <summary>
  /// Docker API implementation of IImageDriver. Pull and push are in the Registry partial,
  /// image build in the Build partial, and save/load/import in the Transfer partial.
  /// </summary>
  public partial class DockerApiImageDriver : DockerApiDriverBase, IImageDriver
  {
    /// <summary>Creates the driver over an existing Docker API connection.</summary>
    public DockerApiImageDriver(IDockerApiConnection connection) : base(connection) { }
    #region List/Inspect Operations
    /// <summary>Lists images via GET /images/json.</summary>
    public async Task<CommandResponse<IList<Image>>> ListAsync(
        DriverContext context, ImageListFilter? filter = null,
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
          var json = JsonHelper.Serialize(filters);
          query.Add($"filters={Uri.EscapeDataString(json)}");
        }
      }
      var path = "/images/json";
      if (query.Count > 0)
        path += "?" + string.Join("&", query);

      var result = await GetJsonElementAsync(path, cancellationToken).ConfigureAwait(false);
      if (!result.Success)
        return CommandResponse<IList<Image>>.Fail(result.ErrorMessage,
            MapHttpErrorCode(result.StatusCode),
            CreateErrorContext("GET /images/json", result.StatusCode, result.ResponseBody),
            result.StatusCode);

      var images = result.Data.ValueKind == JsonValueKind.Array
          ? result.Data.EnumerateArray().Select(ParseImage).ToList()
          : new List<Image>();
      return CommandResponse<IList<Image>>.Ok(images);
    }

    /// <summary>Inspects an image via GET /images/{name}/json.</summary>
    public async Task<CommandResponse<Image>> InspectAsync(
        DriverContext context, string imageId,
        CancellationToken cancellationToken = default)
    {
      var path = $"/images/{Uri.EscapeDataString(imageId)}/json";
      var result = await GetJsonElementAsync(path, cancellationToken).ConfigureAwait(false);
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
      var result = await GetJsonElementAsync(path, cancellationToken).ConfigureAwait(false);
      if (!result.Success)
        return CommandResponse<IList<ImageLayer>>.Fail(result.ErrorMessage,
            MapNotFoundErrorCode(result.StatusCode, ErrorCodes.Image.HistoryFailed),
            CreateErrorContext($"GET {path}", result.StatusCode, result.ResponseBody),
            result.StatusCode);

      var layers = result.Data.ValueKind == JsonValueKind.Array
          ? result.Data.EnumerateArray().Select(ParseImageLayer).ToList()
          : new List<ImageLayer>();
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

      var result = await PostAsync(path, null, cancellationToken).ConfigureAwait(false);
      if (!result.Success)
        return CommandResponse<Unit>.Fail(result.ErrorMessage,
            MapNotFoundErrorCode(result.StatusCode, ErrorCodes.Image.TagFailed),
            CreateErrorContext($"POST /images/{imageId}/tag", result.StatusCode, result.ResponseBody),
            result.StatusCode);

      return CommandResponse<Unit>.Ok(Unit.Default);
    }

    /// <summary>Removes an image via DELETE /images/{name}.</summary>
    /// <remarks>Large remove/prune operations use the 5-minute buffered client and may synthesize 408 while daemon work continues; use WithRequestTimeout.</remarks>
    public async Task<CommandResponse<ImageRemoveResult>> RemoveAsync(
        DriverContext context, string imageId, bool force = false, bool noPrune = false,
        CancellationToken cancellationToken = default)
    {
      var path = $"/images/{Uri.EscapeDataString(imageId)}" +
                 $"?force={force.ToString().ToLowerInvariant()}" +
                 $"&noprune={noPrune.ToString().ToLowerInvariant()}";

      // DELETE /images returns a JSON array; use Connection directly
      // since base DeleteAsync returns ApiResult without body parsing.
      HttpResponseMessage response;
      try
      {
        response = await Connection.DeleteAsync(path, cancellationToken).ConfigureAwait(false);
      }
      catch (Exception ex) when (ex is HttpRequestException
          or System.Net.Sockets.SocketException
          or DriverException { ErrorCode: ErrorCodes.Api.UnsupportedVersion } ||
          ex is TaskCanceledException && !cancellationToken.IsCancellationRequested)
      {
        // The typed unsupported-daemon-version negotiation failure is deliberately rethrown by the
        // connection so the request helpers map it to CommandResponse.Fail; DescribeTransportFailure
        // synthesizes 505 -> Api.UnsupportedVersion here rather than letting it escape raw (DAPI-2).
        var (statusCode, message) = DescribeTransportFailure(ex);
        return CommandResponse<ImageRemoveResult>.Fail(
            message,
            MapHttpErrorCode(statusCode),
            CreateErrorContext($"DELETE /images/{imageId}", statusCode),
            statusCode);
      }
      using var responseToDispose = response;
      var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
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
        // The daemon already committed the delete (2xx); a malformed summary body only costs
        // the Deleted/Untagged detail, so it must not turn the success into a raw JsonException.
        try
        {
          var items = JsonHelper.ParseElement(body);
          if (items.ValueKind == JsonValueKind.Array)
          {
            foreach (var item in items.EnumerateArray())
            {
              var deleted = item.GetStringOrDefault("Deleted");
              var untagged = item.GetStringOrDefault("Untagged");
              if (!string.IsNullOrEmpty(deleted))
                removeResult.Deleted.Add(deleted);
              if (!string.IsNullOrEmpty(untagged))
                removeResult.Untagged.Add(untagged);
            }
          }
        }
        catch (JsonException ex)
        {
          Logger.LogDebug(ex, "Ignoring malformed body of successful DELETE /images response");
        }
      }

      return CommandResponse<ImageRemoveResult>.Ok(removeResult);
    }

    /// <summary>Prunes unused images via POST /images/prune.</summary>
    /// <remarks>Large remove/prune operations use the 5-minute buffered client and may synthesize 408 while daemon work continues; use WithRequestTimeout.</remarks>
    public async Task<CommandResponse<ImagePruneResult>> PruneAsync(
        DriverContext context, bool all = false, Dictionary<string, string>? filter = null,
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
        var json = JsonHelper.Serialize(filters);
        path += $"?filters={Uri.EscapeDataString(json)}";
      }

      var result = await PostJsonElementAsync(path, null, cancellationToken).ConfigureAwait(false);
      if (!result.Success)
        return CommandResponse<ImagePruneResult>.Fail(result.ErrorMessage,
            ErrorCodes.Image.PruneFailed,
            CreateErrorContext("POST /images/prune", result.StatusCode, result.ResponseBody),
            result.StatusCode);

      var pruneResult = new ImagePruneResult
      {
        SpaceReclaimed = result.Data.GetInt64OrDefault("SpaceReclaimed")
      };
      var deletedEl = result.Data.Prop("ImagesDeleted");
      if (deletedEl?.ValueKind == JsonValueKind.Array)
      {
        foreach (var item in deletedEl.Value.EnumerateArray())
        {
          var d = item.GetStringOrDefault("Deleted");
          var u = item.GetStringOrDefault("Untagged");
          if (!string.IsNullOrEmpty(d))
            pruneResult.ImagesDeleted.Add(d);
          else if (!string.IsNullOrEmpty(u))
            pruneResult.ImagesDeleted.Add(u);
        }
      }

      return CommandResponse<ImagePruneResult>.Ok(pruneResult);
    }

    #endregion

    #region Partial Method Declarations (Pull/Push in Registry partial, Build in Build partial)

    public partial Task<CommandResponse<Unit>> PullAsync(
        DriverContext context, string image, string tag,
        IProgress<ImagePullProgress>? progress, CancellationToken cancellationToken);

    public partial Task<CommandResponse<Unit>> PushAsync(
        DriverContext context, string image,
        IProgress<ImagePushProgress>? progress, CancellationToken cancellationToken);

    public partial Task<CommandResponse<ImageBuildResult>> BuildAsync(
        DriverContext context, ImageBuildConfig config,
        IProgress<ImageBuildProgress>? progress, CancellationToken cancellationToken);

    #endregion

    #region JSON Parsing Helpers

    private static Image ParseImage(JsonElement token)
    {
      if (token.ValueKind != JsonValueKind.Object)
        return new Image();
      return new Image
      {
        Id = token.GetStringOrDefault("Id"),
        ParentId = token.GetStringOrDefault("ParentId") ?? string.Empty,
        RepoTags = token.GetStringArray("RepoTags").ToList(),
        RepoDigests = token.GetStringArray("RepoDigests").ToList(),
        Created = DateTimeOffset.FromUnixTimeSeconds(
              token.GetInt64OrDefault("Created")).UtcDateTime,
        Size = token.GetInt64OrDefault("Size"),
        VirtualSize = token.GetInt64OrDefault("VirtualSize"),
        Labels = token.GetStringDictionary("Labels"),
        Containers = token.GetInt32OrDefault("Containers", -1)
      };
    }

    private static Image ParseInspectImage(JsonElement token)
    {
      if (token.ValueKind != JsonValueKind.Object)
        return new Image();
      return new Image
      {
        Id = token.GetStringOrDefault("Id"),
        ParentId = token.GetStringOrDefault("Parent") ?? string.Empty,
        RepoTags = token.GetStringArray("RepoTags").ToList(),
        RepoDigests = token.GetStringArray("RepoDigests").ToList(),
        Created = token.GetDateTimeOrDefault("Created"),
        Size = token.GetInt64OrDefault("Size"),
        VirtualSize = token.GetInt64OrDefault("VirtualSize"),
        Architecture = token.GetStringOrDefault("Architecture"),
        Os = token.GetStringOrDefault("Os"),
        Labels = ExtractLabels(token),
        Containers = -1
      };
    }

    private static Dictionary<string, string> ExtractLabels(JsonElement token)
    {
      var config = token.Prop("Config");
      if (config == null || config.Value.ValueKind != JsonValueKind.Object)
        return new Dictionary<string, string>();
      return config.Value.GetStringDictionary("Labels");
    }

    private static ImageLayer ParseImageLayer(JsonElement token)
    {
      if (token.ValueKind != JsonValueKind.Object)
        return new ImageLayer();
      return new ImageLayer
      {
        Id = token.GetStringOrDefault("Id") ?? string.Empty,
        CreatedBy = token.GetStringOrDefault("CreatedBy") ?? string.Empty,
        Created = DateTimeOffset.FromUnixTimeSeconds(
              token.GetInt64OrDefault("Created")).UtcDateTime,
        Size = token.GetInt64OrDefault("Size"),
        Comment = token.GetStringOrDefault("Comment") ?? string.Empty,
        Tags = token.GetStringArray("Tags").ToList()
      };
    }

    private static Dictionary<string, List<string>> BuildListFilters(ImageListFilter filter)
    {
      var filters = new Dictionary<string, List<string>>();
      if (filter.Dangling.HasValue)
        filters["dangling"] = new List<string> { filter.Dangling.Value.ToString().ToLowerInvariant() };
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

    #endregion
  }
}
