using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers.Docker.Api.Connection;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Drivers.Docker.Api.Components
{
  /// <summary>
  /// Docker API implementation of IServiceDriver for Docker Swarm services.
  /// Uses /services and /tasks endpoints.
  /// </summary>
  public class DockerApiServiceDriver(IDockerApiConnection connection) : DockerApiDriverBase(connection), IServiceDriver
  {
    /// <inheritdoc />
    public async Task<CommandResponse<ServiceCreateResult>> CreateAsync(
        DriverContext context, ServiceCreateConfig config,
        CancellationToken cancellationToken = default)
    {
      var body = BuildServiceSpec(config);
      var result = await PostJsonElementAsync(
          "/services/create", body, DockerApiRegistryAuth.HeaderFor(Connection, config.Image),
          cancellationToken).ConfigureAwait(false);
      if (!result.Success)
        return CommandResponse<ServiceCreateResult>.Fail(result.ErrorMessage,
            result.StatusCode is 599 or 408
                ? MapHttpErrorCode(result.StatusCode)
                : ErrorCodes.Service.CreateFailed,
            CreateErrorContext("POST /services/create", result.StatusCode, result.ResponseBody),
            result.StatusCode);

      return CommandResponse<ServiceCreateResult>.Ok(new ServiceCreateResult
      {
        Id = result.Data.GetStringOrDefault("ID"),
        Warnings = result.Data.Prop("Warnings")?.ValueKind == JsonValueKind.Array
            ? result.Data.Prop("Warnings").Value.Deserialize<List<string>>()
            : []
      });
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> RemoveAsync(
        DriverContext context, string[] serviceIds,
        CancellationToken cancellationToken = default)
    {
      foreach (var id in serviceIds)
      {
        var result = await DeleteAsync(
            $"/services/{Uri.EscapeDataString(id)}", cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<Unit>.Fail(result.ErrorMessage,
              MapNotFoundErrorCode(result.StatusCode, ErrorCodes.Service.NotFound),
              CreateErrorContext($"DELETE /services/{id}", result.StatusCode, result.ResponseBody),
              result.StatusCode);
      }

      throw new InvalidOperationException("unreachable: retry loop always returns");
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> UpdateAsync(
        DriverContext context, string serviceId, ServiceUpdateConfig config,
        CancellationToken cancellationToken = default)
    {
      var escapedId = Uri.EscapeDataString(serviceId);
      for (var attempt = 0; attempt < 2; attempt++)
      {
        var inspectResult = await InspectAsync(
            context, serviceId, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!inspectResult.Success)
          return CommandResponse<Unit>.Fail(inspectResult.Error, inspectResult.ErrorCode);

        var version = inspectResult.Data.Version;
        var body = BuildUpdateSpec(inspectResult.Data, config);
        var image = config.Image ?? inspectResult.Data.Image;
        var result = await PostAsync(
            $"/services/{escapedId}/update?version={Uri.EscapeDataString(version.ToString(CultureInfo.InvariantCulture))}",
            body, DockerApiRegistryAuth.HeaderFor(Connection, image), cancellationToken).ConfigureAwait(false);
        if (result.Success)
          return CommandResponse<Unit>.Ok(Unit.Default);

        if (attempt == 0 && IsVersionConflict(result))
          continue;

        return CommandResponse<Unit>.Fail(result.ErrorMessage,
            result.StatusCode is 599 or 408
                ? MapHttpErrorCode(result.StatusCode)
                : ErrorCodes.Service.UpdateFailed,
            CreateErrorContext($"POST /services/{serviceId}/update", result.StatusCode, result.ResponseBody),
            result.StatusCode);
      }

      return CommandResponse<Unit>.Ok(Unit.Default);
    }

    private static bool IsVersionConflict(ApiResult result) =>
        result.StatusCode == 409 ||
        (result.StatusCode >= 500 &&
         (ContainsVersionConflict(result.ErrorMessage) ||
          ContainsVersionConflict(result.ResponseBody)));

    private static bool ContainsVersionConflict(string value) =>
        value?.Contains("update out of sequence", StringComparison.OrdinalIgnoreCase) == true;

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> RollbackAsync(
        DriverContext context, string serviceId, bool detach = false,
        CancellationToken cancellationToken = default)
    {
      var inspectResult = await InspectAsync(context, serviceId, cancellationToken: cancellationToken).ConfigureAwait(false);
      if (!inspectResult.Success)
        return CommandResponse<Unit>.Fail(inspectResult.Error, inspectResult.ErrorCode);

      var version = inspectResult.Data.Version;
      var escapedId = Uri.EscapeDataString(serviceId);
      var result = await PostAsync(
          $"/services/{escapedId}/update?version={Uri.EscapeDataString(version.ToString(CultureInfo.InvariantCulture))}&rollback=previous",
          new { }, cancellationToken).ConfigureAwait(false);
      if (!result.Success)
        return CommandResponse<Unit>.Fail(result.ErrorMessage,
            ErrorCodes.Service.RollbackFailed,
            CreateErrorContext($"POST /services/{serviceId}/update (rollback)", result.StatusCode),
            result.StatusCode);

      return CommandResponse<Unit>.Ok(Unit.Default);
    }

    /// <inheritdoc />
    public async Task<CommandResponse<IList<ServiceInfo>>> ListAsync(
        DriverContext context, ServiceListFilter filter = null,
        CancellationToken cancellationToken = default)
    {
      var path = "/services";
      if (filter != null)
      {
        var filters = new Dictionary<string, List<string>>();
        if (!string.IsNullOrEmpty(filter.Name))
          filters["name"] = [filter.Name];
        if (!string.IsNullOrEmpty(filter.Id))
          filters["id"] = [filter.Id];
        if (!string.IsNullOrEmpty(filter.Mode))
          filters["mode"] = [filter.Mode];
        if (filters.Count > 0)
          path += $"?filters={Uri.EscapeDataString(JsonHelper.Serialize(filters))}";
      }

      var result = await GetJsonElementAsync(path, cancellationToken).ConfigureAwait(false);
      if (!result.Success)
        return CommandResponse<IList<ServiceInfo>>.Fail(result.ErrorMessage,
            ErrorCodes.Service.ListFailed,
            CreateErrorContext("GET /services", result.StatusCode, result.ResponseBody),
            result.StatusCode);

      var services = result.Data.ValueKind == JsonValueKind.Array
          ? result.Data.EnumerateArray().Select(ParseServiceInfo).ToList()
          : [];
      return CommandResponse<IList<ServiceInfo>>.Ok(services);
    }

    /// <inheritdoc />
    public async Task<CommandResponse<ServiceDetails>> InspectAsync(
        DriverContext context, string serviceId, bool pretty = false,
        CancellationToken cancellationToken = default)
    {
      var result = await GetJsonElementAsync(
          $"/services/{Uri.EscapeDataString(serviceId)}", cancellationToken).ConfigureAwait(false);
      if (!result.Success)
        return CommandResponse<ServiceDetails>.Fail(result.ErrorMessage,
            MapNotFoundErrorCode(result.StatusCode, ErrorCodes.Service.NotFound),
            CreateErrorContext($"GET /services/{serviceId}", result.StatusCode, result.ResponseBody),
            result.StatusCode);

      return CommandResponse<ServiceDetails>.Ok(ParseServiceDetails(result.Data));
    }

    /// <inheritdoc />
    public async Task<CommandResponse<IList<ServiceTask>>> GetTasksAsync(
        DriverContext context, string serviceId,
        ServiceTaskFilter filter = null,
        CancellationToken cancellationToken = default)
    {
      var filters = new Dictionary<string, List<string>> { ["service"] = [serviceId] };
      if (filter?.DesiredState != null)
        filters["desired-state"] = [filter.DesiredState];
      var path = $"/tasks?filters={Uri.EscapeDataString(JsonHelper.Serialize(filters))}";

      var result = await GetJsonElementAsync(path, cancellationToken).ConfigureAwait(false);
      if (!result.Success)
        return CommandResponse<IList<ServiceTask>>.Fail(result.ErrorMessage,
            ErrorCodes.Service.TasksFailed,
            CreateErrorContext("GET /tasks", result.StatusCode, result.ResponseBody),
            result.StatusCode);

      var tasks = result.Data.ValueKind == JsonValueKind.Array
          ? result.Data.EnumerateArray().Select(ParseServiceTask).ToList()
          : [];
      return CommandResponse<IList<ServiceTask>>.Ok(tasks);
    }

    /// <inheritdoc />
    public async Task<CommandResponse<string>> GetLogsAsync(
        DriverContext context, string serviceId,
        ServiceLogsConfig config = null,
        CancellationToken cancellationToken = default)
    {
      config ??= new ServiceLogsConfig();
      if (config.Follow)
        return CommandResponse<string>.Fail(
            "GetLogsAsync does not support follow=true because Docker API service logs can stream indefinitely.",
            ErrorCodes.Service.LogsFailed,
            CreateErrorContext($"GET /services/{serviceId}/logs", 0));

      var path = $"/services/{Uri.EscapeDataString(serviceId)}/logs?stdout=true&stderr=true";
      if (config.Tail.HasValue)
        path += $"&tail={config.Tail.Value}";
      if (config.Timestamps)
        path += "&timestamps=true";
      if (!string.IsNullOrEmpty(config.Since))
        path += $"&since={Uri.EscapeDataString(config.Since)}";

      try
      {
        using var stream = await GetRawStreamAsync(path, cancellationToken).ConfigureAwait(false);
        var logs = await ReadDockerLogTailAsync(stream, cancellationToken).ConfigureAwait(false);
        return CommandResponse<string>.Ok(logs);
      }
      catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<string>.Fail(
            $"Failed to get logs for service '{serviceId}': {ex.Message}",
            ErrorCodes.Service.LogsFailed,
            CreateErrorContext($"GET /services/{serviceId}/logs", 0));
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> ScaleAsync(
        DriverContext context, Dictionary<string, int> serviceReplicas,
        bool detach = false, CancellationToken cancellationToken = default)
    {
      foreach (var (serviceId, replicas) in serviceReplicas)
      {
        var updateConfig = new ServiceUpdateConfig { Replicas = replicas };
        var result = await UpdateAsync(context, serviceId, updateConfig, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return result;
      }

      return CommandResponse<Unit>.Ok(Unit.Default);
    }

    #region Spec Builders

    private static Dictionary<string, object> BuildServiceSpec(ServiceCreateConfig config)
    {
      var spec = new Dictionary<string, object>
      {
        ["Name"] = config.Name,
        ["TaskTemplate"] = new Dictionary<string, object>
        {
          ["ContainerSpec"] = BuildContainerSpec(config)
        }
      };

      if (config.Replicas.HasValue || !string.IsNullOrEmpty(config.Mode))
      {
        var mode = string.Equals(config.Mode, "global", StringComparison.OrdinalIgnoreCase)
            ? new Dictionary<string, object> { ["Global"] = new { } }
            : new Dictionary<string, object>
            {
              ["Replicated"] = new { Replicas = config.Replicas ?? 1 }
            };
        spec["Mode"] = mode;
      }

      if (config.Networks?.Count > 0)
        spec["Networks"] = config.Networks.Select(n =>
            new Dictionary<string, string> { ["Target"] = n }).ToList();

      if (config.Ports?.Count > 0)
        spec["EndpointSpec"] = new Dictionary<string, object>
        {
          ["Ports"] = config.Ports.Select(p => new
          {
            p.TargetPort,
            p.PublishedPort,
            Protocol = p.Protocol ?? "tcp",
            PublishMode = p.PublishMode ?? "ingress"
          }).ToList()
        };

      if (config.Labels?.Count > 0)
        spec["Labels"] = config.Labels;

      return spec;
    }

    private static Dictionary<string, object> BuildContainerSpec(ServiceCreateConfig config)
    {
      var containerSpec = new Dictionary<string, object> { ["Image"] = config.Image };

      if (config.Command?.Length > 0)
        containerSpec["Command"] = config.Command;
      if (config.Args?.Length > 0)
        containerSpec["Args"] = config.Args;
      if (!string.IsNullOrEmpty(config.User))
        containerSpec["User"] = config.User;
      if (!string.IsNullOrEmpty(config.WorkDir))
        containerSpec["Dir"] = config.WorkDir;

      if (config.Environment?.Count > 0)
        containerSpec["Env"] = config.Environment
            .Select(kv => $"{kv.Key}={kv.Value}").ToArray();

      if (config.Mounts?.Count > 0)
        containerSpec["Mounts"] = config.Mounts.Select(m => new
        {
          Type = m.Type ?? "volume",
          m.Source,
          m.Target,
          m.ReadOnly
        }).ToList();

      return containerSpec;
    }

    private static JsonObject BuildUpdateSpec(ServiceDetails current, ServiceUpdateConfig config)
    {
      var root = string.IsNullOrWhiteSpace(current.RawJson)
          ? null
          : JsonNode.Parse(current.RawJson)?.AsObject();
      var spec = root?["Spec"]?.DeepClone().AsObject() ?? [];
      if (config.Replicas.HasValue)
      {
        var mode = ObjectAt(spec, "Mode");
        var replicated = ObjectAt(mode, "Replicated");
        replicated["Replicas"] = config.Replicas.Value;
      }
      if (!string.IsNullOrEmpty(config.Image))
        ObjectAt(ObjectAt(spec, "TaskTemplate"), "ContainerSpec")["Image"] = config.Image;
      if (config.LabelAdd?.Count > 0 || config.LabelRm?.Count > 0)
        MutateStringMap(ObjectAt(spec, "Labels"), config.LabelAdd, config.LabelRm);
      if (config.EnvAdd?.Count > 0 || config.EnvRm?.Count > 0)
        MutateEnv(ObjectAt(ObjectAt(spec, "TaskTemplate"), "ContainerSpec"), config.EnvAdd, config.EnvRm);

      return spec;
    }

    private static JsonObject ObjectAt(JsonObject parent, string name)
    {
      if (parent[name] is JsonObject obj)
        return obj;
      obj = [];
      parent[name] = obj;
      return obj;
    }

    private static void MutateStringMap(
        JsonObject target, Dictionary<string, string> add, List<string> remove)
    {
      foreach (var key in remove ?? [])
        target.Remove(key);
      foreach (var (key, value) in add ?? [])
        target[key] = value;
    }

    private static void MutateEnv(
        JsonObject containerSpec, Dictionary<string, string> add, List<string> remove)
    {
      var values = new List<string>();
      if (containerSpec["Env"] is JsonArray env)
      {
        foreach (var item in env)
        {
          var text = item?.GetValue<string>();
          if (!string.IsNullOrEmpty(text))
            values.Add(text);
        }
      }
      foreach (var key in remove ?? [])
        values.RemoveAll(value => string.Equals(EnvKey(value), key, StringComparison.Ordinal));
      foreach (var (key, value) in add ?? [])
      {
        values.RemoveAll(existing => string.Equals(EnvKey(existing), key, StringComparison.Ordinal));
        values.Add($"{key}={value}");
      }
      containerSpec["Env"] = new JsonArray(
          [.. values.Select(static value => JsonValue.Create(value))]);
    }

    private static string EnvKey(string value)
    {
      var equals = value.IndexOf('=');
      return equals < 0 ? value : value[..equals];
    }

    #endregion

    #region JSON Parsing

    private static ServiceInfo ParseServiceInfo(JsonElement token)
    {
      var spec = token.Prop("Spec");
      var containerSpec = spec?.Prop("TaskTemplate")?.Prop("ContainerSpec");
      return new ServiceInfo
      {
        Id = token.GetStringOrDefault("ID"),
        Name = spec?.GetStringOrDefault("Name"),
        Image = containerSpec?.GetStringOrDefault("Image"),
        Mode = spec?.Prop("Mode")?.Prop("Replicated") != null ? "replicated" : "global"
      };
    }

    private static ServiceDetails ParseServiceDetails(JsonElement json)
    {
      if (json.ValueKind != JsonValueKind.Object)
        return new ServiceDetails();
      var spec = json.Prop("Spec");
      var containerSpec = spec?.Prop("TaskTemplate")?.Prop("ContainerSpec");
      var version = json.Prop("Version");

      var replicatedEl = spec?.Prop("Mode")?.Prop("Replicated");

      return new ServiceDetails
      {
        Id = json.GetStringOrDefault("ID"),
        Version = version?.GetInt64OrDefault("Index") ?? 0,
        Name = spec?.GetStringOrDefault("Name"),
        Image = containerSpec?.GetStringOrDefault("Image"),
        Mode = replicatedEl != null ? "replicated" : "global",
        Replicas = (int)(replicatedEl?.GetInt64OrDefault("Replicas") ?? 0),
        CreatedAt = json.GetDateTimeOrDefault("CreatedAt"),
        UpdatedAt = json.GetDateTimeOrDefault("UpdatedAt"),
        RawJson = JsonSerializer.Serialize(json, JsonHelper.IndentedOptions)
      };
    }

    private static ServiceTask ParseServiceTask(JsonElement token)
    {
      var containerSpec = token.Prop("Spec")?.Prop("ContainerSpec");
      var status = token.Prop("Status");
      return new ServiceTask
      {
        Id = token.GetStringOrDefault("ID"),
        Name = token.GetStringOrDefault("Name"),
        Image = containerSpec?.GetStringOrDefault("Image"),
        Node = token.GetStringOrDefault("NodeID"),
        DesiredState = token.GetStringOrDefault("DesiredState"),
        CurrentState = status?.GetStringOrDefault("State"),
        Error = status?.GetStringOrDefault("Err")
      };
    }

    #endregion
  }
}
