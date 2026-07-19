#nullable disable warnings
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentDocker.Common;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Drivers.Docker.Api.Components
{
  /// <summary>
  /// Request-spec builders and JSON parsing helpers for DockerApiServiceDriver.
  /// </summary>
  public partial class DockerApiServiceDriver
  {
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
      var mode = spec?.Prop("Mode");
      var replicas = ParseServiceReplicas(mode);
      return new ServiceInfo
      {
        Id = token.GetStringOrDefault("ID"),
        Name = spec?.GetStringOrDefault("Name"),
        Image = containerSpec?.GetStringOrDefault("Image"),
        Mode = ParseServiceMode(mode),
        Replicas = replicas > 0
            ? replicas.ToString(CultureInfo.InvariantCulture)
            : null
      };
    }

    private static ServiceDetails ParseServiceDetails(JsonElement json)
    {
      if (json.ValueKind != JsonValueKind.Object)
        return new ServiceDetails();
      var spec = json.Prop("Spec");
      var containerSpec = spec?.Prop("TaskTemplate")?.Prop("ContainerSpec");
      var version = json.Prop("Version");

      var mode = spec?.Prop("Mode");

      return new ServiceDetails
      {
        Id = json.GetStringOrDefault("ID"),
        Version = version?.GetInt64OrDefault("Index") ?? 0,
        Name = spec?.GetStringOrDefault("Name"),
        Image = containerSpec?.GetStringOrDefault("Image"),
        Mode = ParseServiceMode(mode),
        Replicas = ParseServiceReplicas(mode),
        CreatedAt = json.GetDateTimeOrDefault("CreatedAt"),
        UpdatedAt = json.GetDateTimeOrDefault("UpdatedAt"),
        RawJson = JsonSerializer.Serialize(json, JsonHelper.IndentedOptions)
      };
    }

    private static string ParseServiceMode(JsonElement? mode)
    {
      if (mode?.Prop("Replicated") != null)
        return "replicated";
      if (mode?.Prop("Global") != null)
        return "global";
      if (mode?.Prop("ReplicatedJob") != null)
        return "replicated-job";
      if (mode?.Prop("GlobalJob") != null)
        return "global-job";
      return "global";
    }

    private static int ParseServiceReplicas(JsonElement? mode)
    {
      var replicated = mode?.Prop("Replicated");
      if (replicated != null)
        return (int)replicated.Value.GetInt64OrDefault("Replicas");
      var replicatedJob = mode?.Prop("ReplicatedJob");
      return (int)(replicatedJob?.GetInt64OrDefault("TotalCompletions") ?? 0);
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
