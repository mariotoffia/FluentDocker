using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers.Docker.Api.Connection;
using FluentDocker.Model.Drivers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FluentDocker.Drivers.Docker.Api.Components
{
  /// <summary>
  /// Docker API implementation of IServiceDriver for Docker Swarm services.
  /// Uses /services and /tasks endpoints.
  /// </summary>
  public class DockerApiServiceDriver : DockerApiDriverBase, IServiceDriver
  {
    public DockerApiServiceDriver(IDockerApiConnection connection) : base(connection) { }

    public async Task<CommandResponse<ServiceCreateResult>> CreateAsync(
        DriverContext context, ServiceCreateConfig config,
        CancellationToken cancellationToken = default)
    {
      var body = BuildServiceSpec(config);
      var result = await PostJsonAsync<JObject>("/services/create", body, cancellationToken);
      if (!result.Success)
        return CommandResponse<ServiceCreateResult>.Fail(result.ErrorMessage,
            ErrorCodes.Service.CreateFailed,
            CreateErrorContext("POST /services/create", result.StatusCode, result.ResponseBody),
            result.StatusCode);

      return CommandResponse<ServiceCreateResult>.Ok(new ServiceCreateResult
      {
        Id = result.Data?.Value<string>("ID"),
        Warnings = result.Data?["Warnings"]?.ToObject<List<string>>() ?? new List<string>()
      });
    }

    public async Task<CommandResponse<Unit>> RemoveAsync(
        DriverContext context, string[] serviceIds,
        CancellationToken cancellationToken = default)
    {
      foreach (var id in serviceIds)
      {
        var result = await DeleteAsync($"/services/{id}", cancellationToken);
        if (!result.Success)
          return CommandResponse<Unit>.Fail(result.ErrorMessage,
              MapNotFoundErrorCode(result.StatusCode, ErrorCodes.Service.NotFound),
              CreateErrorContext($"DELETE /services/{id}", result.StatusCode, result.ResponseBody),
              result.StatusCode);
      }

      return CommandResponse<Unit>.Ok(Unit.Default);
    }

    public async Task<CommandResponse<Unit>> UpdateAsync(
        DriverContext context, string serviceId, ServiceUpdateConfig config,
        CancellationToken cancellationToken = default)
    {
      // First get current service spec and version
      var inspectResult = await InspectAsync(context, serviceId, cancellationToken: cancellationToken);
      if (!inspectResult.Success)
        return CommandResponse<Unit>.Fail(inspectResult.Error, inspectResult.ErrorCode);

      var version = inspectResult.Data.Version;
      var body = BuildUpdateSpec(inspectResult.Data, config);

      var result = await PostAsync(
          $"/services/{serviceId}/update?version={version}", body, cancellationToken);
      if (!result.Success)
        return CommandResponse<Unit>.Fail(result.ErrorMessage,
            ErrorCodes.Service.UpdateFailed,
            CreateErrorContext($"POST /services/{serviceId}/update", result.StatusCode, result.ResponseBody),
            result.StatusCode);

      return CommandResponse<Unit>.Ok(Unit.Default);
    }

    public async Task<CommandResponse<Unit>> RollbackAsync(
        DriverContext context, string serviceId, bool detach = false,
        CancellationToken cancellationToken = default)
    {
      var inspectResult = await InspectAsync(context, serviceId, cancellationToken: cancellationToken);
      if (!inspectResult.Success)
        return CommandResponse<Unit>.Fail(inspectResult.Error, inspectResult.ErrorCode);

      var version = inspectResult.Data.Version;
      var result = await PostAsync(
          $"/services/{serviceId}/update?version={version}&rollback=previous",
          new { }, cancellationToken);
      if (!result.Success)
        return CommandResponse<Unit>.Fail(result.ErrorMessage,
            ErrorCodes.Service.RollbackFailed,
            CreateErrorContext($"POST /services/{serviceId}/update (rollback)", result.StatusCode),
            result.StatusCode);

      return CommandResponse<Unit>.Ok(Unit.Default);
    }

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
          path += $"?filters={Uri.EscapeDataString(JsonConvert.SerializeObject(filters))}";
      }

      var result = await GetJsonAsync<JArray>(path, cancellationToken);
      if (!result.Success)
        return CommandResponse<IList<ServiceInfo>>.Fail(result.ErrorMessage,
            ErrorCodes.Service.ListFailed,
            CreateErrorContext("GET /services", result.StatusCode, result.ResponseBody),
            result.StatusCode);

      var services = result.Data?.Select(ParseServiceInfo).ToList()
          ?? new List<ServiceInfo>();
      return CommandResponse<IList<ServiceInfo>>.Ok(services);
    }

    public async Task<CommandResponse<ServiceDetails>> InspectAsync(
        DriverContext context, string serviceId, bool pretty = false,
        CancellationToken cancellationToken = default)
    {
      var result = await GetJsonAsync<JObject>($"/services/{serviceId}", cancellationToken);
      if (!result.Success)
        return CommandResponse<ServiceDetails>.Fail(result.ErrorMessage,
            MapNotFoundErrorCode(result.StatusCode, ErrorCodes.Service.NotFound),
            CreateErrorContext($"GET /services/{serviceId}", result.StatusCode, result.ResponseBody),
            result.StatusCode);

      return CommandResponse<ServiceDetails>.Ok(ParseServiceDetails(result.Data));
    }

    public async Task<CommandResponse<IList<ServiceTask>>> GetTasksAsync(
        DriverContext context, string serviceId,
        ServiceTaskFilter filter = null,
        CancellationToken cancellationToken = default)
    {
      var filters = new Dictionary<string, List<string>> { ["service"] = [serviceId] };
      if (filter?.DesiredState != null)
        filters["desired-state"] = [filter.DesiredState];
      var path = $"/tasks?filters={Uri.EscapeDataString(JsonConvert.SerializeObject(filters))}";

      var result = await GetJsonAsync<JArray>(path, cancellationToken);
      if (!result.Success)
        return CommandResponse<IList<ServiceTask>>.Fail(result.ErrorMessage,
            ErrorCodes.Service.TasksFailed,
            CreateErrorContext("GET /tasks", result.StatusCode, result.ResponseBody),
            result.StatusCode);

      var tasks = result.Data?.Select(ParseServiceTask).ToList()
          ?? new List<ServiceTask>();
      return CommandResponse<IList<ServiceTask>>.Ok(tasks);
    }

    public async Task<CommandResponse<string>> GetLogsAsync(
        DriverContext context, string serviceId,
        ServiceLogsConfig config = null,
        CancellationToken cancellationToken = default)
    {
      config ??= new ServiceLogsConfig();
      var path = $"/services/{serviceId}/logs?stdout=true&stderr=true";
      if (config.Tail.HasValue)
        path += $"&tail={config.Tail.Value}";
      if (config.Timestamps)
        path += "&timestamps=true";
      if (!string.IsNullOrEmpty(config.Since))
        path += $"&since={config.Since}";

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
            $"Failed to get logs for service '{serviceId}': {ex.Message}",
            ErrorCodes.Service.LogsFailed,
            CreateErrorContext($"GET /services/{serviceId}/logs", 0));
      }
    }

    public async Task<CommandResponse<Unit>> ScaleAsync(
        DriverContext context, Dictionary<string, int> serviceReplicas,
        bool detach = false, CancellationToken cancellationToken = default)
    {
      foreach (var (serviceId, replicas) in serviceReplicas)
      {
        var updateConfig = new ServiceUpdateConfig { Replicas = replicas };
        var result = await UpdateAsync(context, serviceId, updateConfig, cancellationToken);
        if (!result.Success)
          return result;
      }

      return CommandResponse<Unit>.Ok(Unit.Default);
    }

    #region Spec Builders

    private static object BuildServiceSpec(ServiceCreateConfig config)
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
        var mode = config.Mode?.ToLower() == "global"
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
            TargetPort = p.TargetPort,
            PublishedPort = p.PublishedPort,
            Protocol = p.Protocol ?? "tcp",
            PublishMode = p.PublishMode ?? "ingress"
          }).ToList()
        };

      if (config.Labels?.Count > 0)
        spec["Labels"] = config.Labels;

      return spec;
    }

    private static object BuildContainerSpec(ServiceCreateConfig config)
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
          Source = m.Source,
          Target = m.Target,
          ReadOnly = m.ReadOnly
        }).ToList();

      return containerSpec;
    }

    private static object BuildUpdateSpec(ServiceDetails current, ServiceUpdateConfig config)
    {
      // Build a minimal update spec based on what changed
      var spec = new Dictionary<string, object>();
      // The actual implementation would merge current spec with updates
      // For now, return a basic spec with replicas change
      if (config.Replicas.HasValue)
      {
        spec["Mode"] = new Dictionary<string, object>
        {
          ["Replicated"] = new { Replicas = config.Replicas.Value }
        };
      }

      return spec;
    }

    #endregion

    #region JSON Parsing

    private static ServiceInfo ParseServiceInfo(JToken token)
    {
      var spec = token["Spec"] as JObject;
      return new ServiceInfo
      {
        Id = token.Value<string>("ID"),
        Name = spec?.Value<string>("Name"),
        Image = (spec?["TaskTemplate"]?["ContainerSpec"] as JObject)?.Value<string>("Image"),
        Mode = spec?["Mode"]?["Replicated"] != null ? "replicated" : "global"
      };
    }

    private static ServiceDetails ParseServiceDetails(JObject json)
    {
      if (json == null)
        return new ServiceDetails();
      var spec = json["Spec"] as JObject;
      var containerSpec = spec?["TaskTemplate"]?["ContainerSpec"] as JObject;
      var version = json["Version"] as JObject;

      return new ServiceDetails
      {
        Id = json.Value<string>("ID"),
        Version = version?.Value<long?>("Index") ?? 0,
        Name = spec?.Value<string>("Name"),
        Image = containerSpec?.Value<string>("Image"),
        Mode = spec?["Mode"]?["Replicated"] != null ? "replicated" : "global",
        Replicas = (int?)(spec?["Mode"]?["Replicated"]?.Value<long?>("Replicas")) ?? 0,
        CreatedAt = json.Value<DateTime?>("CreatedAt") ?? DateTime.MinValue,
        UpdatedAt = json.Value<DateTime?>("UpdatedAt") ?? DateTime.MinValue,
        RawJson = json.ToString(Formatting.Indented)
      };
    }

    private static ServiceTask ParseServiceTask(JToken token)
    {
      return new ServiceTask
      {
        Id = token.Value<string>("ID"),
        Name = token.Value<string>("Name"),
        Image = (token["Spec"]?["ContainerSpec"] as JObject)?.Value<string>("Image"),
        Node = token.Value<string>("NodeID"),
        DesiredState = token.Value<string>("DesiredState"),
        CurrentState = (token["Status"] as JObject)?.Value<string>("State"),
        Error = (token["Status"] as JObject)?.Value<string>("Err")
      };
    }

    #endregion
  }
}
