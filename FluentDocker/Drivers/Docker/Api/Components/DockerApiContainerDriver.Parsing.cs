using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Api.ApiModels;
using FluentDocker.Model.Containers;
using Container = FluentDocker.Model.Containers.Container;
using ContainerConfig = FluentDocker.Model.Containers.ContainerConfig;
using ContainerState = FluentDocker.Model.Containers.ContainerState;

namespace FluentDocker.Drivers.Docker.Api.Components
{
  /// <summary>
  /// Request building and JSON parsing helpers for DockerApiContainerDriver.
  /// </summary>
  public partial class DockerApiContainerDriver
  {
    #region Request Building

    private static CreateContainerRequest BuildCreateRequest(ContainerCreateConfig config)
    {
      var request = new CreateContainerRequest
      {
        Image = config.Image,
        Cmd = config.Command,
        Entrypoint = config.Entrypoint,
        WorkingDir = config.WorkingDirectory,
        User = config.User,
        Hostname = config.Hostname,
        Tty = config.Tty,
        OpenStdin = config.Interactive,
        StopSignal = config.StopSignal,
        StopTimeout = config.StopTimeout,
        Labels = config.Labels?.Count > 0 ? config.Labels : null,
        HostConfig = BuildHostConfig(config)
      };

      if (config.Environment?.Count > 0)
        request.Env = config.Environment
            .Select(kv => $"{kv.Key}={kv.Value}").ToArray();

      if (config.PortBindings?.Count > 0)
      {
        request.ExposedPorts = new Dictionary<string, object>();
        foreach (var port in config.PortBindings.Keys)
        {
          var key = port.Contains('/') ? port : $"{port}/tcp";
          request.ExposedPorts[key] = new object();
        }
      }

      request.NetworkingConfig = BuildNetworkingConfig(config);

      if (config.HealthCheck != null)
        request.Healthcheck = BuildHealthcheck(config.HealthCheck);

      if (!string.IsNullOrEmpty(config.Platform))
        request.Platform = config.Platform;

      return request;
    }

    private static HostConfigRequest BuildHostConfig(ContainerCreateConfig config)
    {
      var hc = new HostConfigRequest
      {
        AutoRemove = config.AutoRemove,
        Privileged = config.Privileged,
        Memory = config.MemoryLimit,
        CpuShares = config.CpuShares,
        NetworkMode = config.NetworkMode
      };

      if (config.PortBindings?.Count > 0)
      {
        hc.PortBindings = new Dictionary<string, List<PortBindingRequest>>();
        foreach (var (containerPort, hostPort) in config.PortBindings)
        {
          var key = containerPort.Contains('/')
              ? containerPort : $"{containerPort}/tcp";
          hc.PortBindings[key] = new List<PortBindingRequest>
                    {
                        new() { HostPort = hostPort }
                    };
        }
      }

      if (config.Volumes?.Count > 0)
        hc.Binds = config.Volumes
            .Select(kv => $"{kv.Key}:{kv.Value}").ToArray();

      if (!string.IsNullOrEmpty(config.RestartPolicy))
      {
        var parts = config.RestartPolicy.Split(':');
        hc.RestartPolicy = new RestartPolicyRequest
        {
          Name = parts[0],
          MaximumRetryCount = parts.Length > 1
                && int.TryParse(parts[1], out var max) ? max : 0
        };
      }

      if (config.Dns?.Count > 0)
        hc.Dns = config.Dns.ToArray();

      if (config.ExtraHosts?.Count > 0)
        hc.ExtraHosts = config.ExtraHosts
            .Select(kv => $"{kv.Key}:{kv.Value}").ToArray();

      if (config.Links?.Count > 0)
        hc.Links = config.Links.ToArray();
      if (config.CapAdd?.Count > 0)
        hc.CapAdd = config.CapAdd.ToArray();
      if (config.CapDrop?.Count > 0)
        hc.CapDrop = config.CapDrop.ToArray();
      if (config.SecurityOpt?.Count > 0)
        hc.SecurityOpt = config.SecurityOpt.ToArray();
      if (config.ShmSize.HasValue)
        hc.ShmSize = config.ShmSize;
      if (config.Tmpfs?.Count > 0)
        hc.Tmpfs = config.Tmpfs;
      if (config.Devices?.Count > 0)
        hc.Devices = config.Devices.Select(d => new DeviceMappingRequest
        {
          PathOnHost = d.Key,
          PathInContainer = d.Value
        }).ToList();
      hc.ReadonlyRootfs = config.ReadonlyRootfs;
      if (!string.IsNullOrEmpty(config.Runtime))
        hc.Runtime = config.Runtime;

      return hc;
    }

    private static NetworkingConfigRequest BuildNetworkingConfig(
        ContainerCreateConfig config)
    {
      if (config.Networks == null || config.Networks.Count == 0)
        return null;

      var endpoints = new Dictionary<string, EndpointConfigRequest>();
      foreach (var network in config.Networks)
      {
        var endpoint = new EndpointConfigRequest();
        if (!string.IsNullOrEmpty(config.Ipv4Address) ||
            !string.IsNullOrEmpty(config.Ipv6Address))
        {
          endpoint.IpamConfig = new IpamConfigRequest
          {
            Ipv4Address = config.Ipv4Address,
            Ipv6Address = config.Ipv6Address
          };
        }

        if (config.NetworkAliases != null &&
            config.NetworkAliases.TryGetValue(network, out var aliases) &&
            aliases?.Count > 0)
        {
          endpoint.Aliases = aliases;
        }

        endpoints[network] = endpoint;
      }

      return new NetworkingConfigRequest { EndpointsConfig = endpoints };
    }

    private static HealthcheckRequest BuildHealthcheck(HealthCheckConfig hc)
    {
      return new HealthcheckRequest
      {
        Test = hc.Test,
        Retries = hc.Retries > 0 ? hc.Retries : null,
        Interval = ParseDurationNanoseconds(hc.Interval),
        Timeout = ParseDurationNanoseconds(hc.Timeout),
        StartPeriod = ParseDurationNanoseconds(hc.StartPeriod)
      };
    }

    private static long? ParseDurationNanoseconds(string duration)
    {
      if (string.IsNullOrEmpty(duration))
        return null;

      if (duration.EndsWith("ms") &&
          long.TryParse(duration[..^2], out var ms))
        return ms * 1_000_000;

      if (duration.EndsWith('s') &&
          long.TryParse(duration[..^1], out var sec))
        return sec * 1_000_000_000;

      if (duration.EndsWith('m') &&
          long.TryParse(duration[..^1], out var min))
        return min * 60 * 1_000_000_000;

      return long.TryParse(duration, out var raw) ? raw : null;
    }

    private static string BuildListPath(ContainerListFilter filter)
    {
      var path = "/containers/json";
      var queryParams = new List<string>();

      if (filter?.All == true)
        queryParams.Add("all=true");

      var filters = BuildListFilters(filter);
      if (filters != null)
        queryParams.Add($"filters={Uri.EscapeDataString(filters)}");

      if (filter?.Limit.HasValue == true)
        queryParams.Add($"limit={filter.Limit.Value}");

      if (queryParams.Count > 0)
        path += "?" + string.Join("&", queryParams);

      return path;
    }

    private static string BuildListFilters(ContainerListFilter filter)
    {
      if (filter == null)
        return null;

      var dict = new Dictionary<string, List<string>>();
      if (!string.IsNullOrEmpty(filter.Status))
        dict["status"] = new List<string> { filter.Status };
      if (!string.IsNullOrEmpty(filter.Name))
        dict["name"] = new List<string> { filter.Name };
      if (!string.IsNullOrEmpty(filter.Id))
        dict["id"] = new List<string> { filter.Id };
      if (!string.IsNullOrEmpty(filter.Ancestor))
        dict["ancestor"] = new List<string> { filter.Ancestor };
      if (filter.Labels?.Count > 0)
      {
        dict["label"] = filter.Labels
            .Select(kv => string.IsNullOrEmpty(kv.Value)
                ? kv.Key : $"{kv.Key}={kv.Value}")
            .ToList();
      }

      return dict.Count > 0 ? JsonHelper.Serialize(dict) : null;
    }

    #endregion

    #region JSON Parsing

    private static Container ParseContainerInspect(JsonElement json)
    {
      if (json.ValueKind != JsonValueKind.Object)
        return new Container();
      return new Container
      {
        Id = json.GetStringOrDefault("Id"),
        Name = json.GetStringOrDefault("Name")?.TrimStart('/'),
        Image = json.GetStringOrDefault("Image"),
        Created = json.GetDateTimeOrDefault("Created"),
        Driver = json.GetStringOrDefault("Driver"),
        State = ParseContainerState(json.Prop("State")),
        Config = ParseContainerConfig(json.Prop("Config")),
        NetworkSettings = ParseNetworkSettings(json.Prop("NetworkSettings"))
      };
    }

    private static ContainerState ParseContainerState(JsonElement? element)
    {
      if (element == null || element.Value.ValueKind != JsonValueKind.Object)
        return new ContainerState();
      var el = element.Value;
      return new ContainerState
      {
        Status = el.GetStringOrDefault("Status"),
        Running = el.GetBoolOrDefault("Running"),
        Paused = el.GetBoolOrDefault("Paused"),
        Restarting = el.GetBoolOrDefault("Restarting"),
        OOMKilled = el.GetBoolOrDefault("OOMKilled"),
        Dead = el.GetBoolOrDefault("Dead"),
        Pid = el.GetInt32OrDefault("Pid"),
        ExitCode = el.GetInt64OrDefault("ExitCode"),
        Error = el.GetStringOrDefault("Error"),
        StartedAt = el.GetDateTimeOrDefault("StartedAt"),
        FinishedAt = el.GetDateTimeOrDefault("FinishedAt")
      };
    }

    private static ContainerConfig ParseContainerConfig(JsonElement? element)
    {
      if (element == null || element.Value.ValueKind != JsonValueKind.Object)
        return null;
      var el = element.Value;
      return new ContainerConfig
      {
        Hostname = el.GetStringOrDefault("Hostname"),
        User = el.GetStringOrDefault("User"),
        Tty = el.GetBoolOrDefault("Tty"),
        OpenStdin = el.GetBoolOrDefault("OpenStdin"),
        Image = el.GetStringOrDefault("Image"),
        WorkingDir = el.GetStringOrDefault("WorkingDir"),
        Cmd = el.GetStringArray("Cmd"),
        EntryPoint = el.GetStringArray("Entrypoint"),
        Env = el.GetStringArray("Env"),
        Labels = el.GetStringDictionary("Labels"),
        StopSignal = el.GetStringOrDefault("StopSignal")
      };
    }

    private static ContainerNetworkSettings ParseNetworkSettings(JsonElement? element)
    {
      if (element == null || element.Value.ValueKind != JsonValueKind.Object)
        return null;
      var el = element.Value;
      var networks = el.Prop("Networks");
      return new ContainerNetworkSettings
      {
        Bridge = el.GetStringOrDefault("Bridge"),
        Gateway = el.GetStringOrDefault("Gateway"),
        IPAddress = el.GetStringOrDefault("IPAddress"),
        MacAddress = el.GetStringOrDefault("MacAddress"),
        Networks = networks?.ValueKind == JsonValueKind.Object
            ? networks.Value.Deserialize<Dictionary<string, BridgeNetwork>>()
            : null
      };
    }

    private static List<Container> ParseContainerList(JsonElement json)
    {
      var containers = new List<Container>();
      if (json.ValueKind != JsonValueKind.Array)
        return containers;

      foreach (var token in json.EnumerateArray())
      {
        var namesEl = token.Prop("Names");
        string firstName = null;
        if (namesEl?.ValueKind == JsonValueKind.Array)
        {
          foreach (var n in namesEl.Value.EnumerateArray())
          {
            firstName = n.GetString()?.TrimStart('/');
            break;
          }
        }

        containers.Add(new Container
        {
          Id = token.GetStringOrDefault("Id"),
          Image = token.GetStringOrDefault("Image"),
          Created = DateTimeOffset
                .FromUnixTimeSeconds(token.GetInt64OrDefault("Created"))
                .UtcDateTime,
          Name = firstName,
          State = new ContainerState
          {
            Status = token.GetStringOrDefault("State")
          }
        });
      }

      return containers;
    }

    private static ContainerStatsResult ParseContainerStats(
        JsonElement json, string containerId)
    {
      if (json.ValueKind != JsonValueKind.Object)
        return new ContainerStatsResult { ContainerId = containerId };

      var stats = new ContainerStatsResult
      {
        ContainerId = containerId,
        Name = json.GetStringOrDefault("name")?.TrimStart('/'),
        Pids = GetPidsCount(json)
      };

      var cpuStats = json.Prop("cpu_stats");
      var preCpu = json.Prop("precpu_stats");
      if (cpuStats != null && preCpu != null)
        stats.CpuPercent = CalculateCpuPercent(cpuStats.Value, preCpu.Value);

      var memStats = json.Prop("memory_stats");
      if (memStats != null && memStats.Value.ValueKind == JsonValueKind.Object)
      {
        stats.MemoryUsage = memStats.Value.GetInt64OrDefault("usage");
        stats.MemoryLimit = memStats.Value.GetInt64OrDefault("limit");
        if (stats.MemoryLimit > 0)
          stats.MemoryPercent =
              (double)stats.MemoryUsage / stats.MemoryLimit * 100.0;
      }

      var netObj = json.Prop("networks");
      if (netObj != null && netObj.Value.ValueKind == JsonValueKind.Object)
      {
        foreach (var prop in netObj.Value.EnumerateObject())
        {
          stats.NetworkRxBytes += prop.Value.GetInt64OrDefault("rx_bytes");
          stats.NetworkTxBytes += prop.Value.GetInt64OrDefault("tx_bytes");
        }
      }

      var blkioStats = json.Prop("blkio_stats");
      var blk = blkioStats?.Prop("io_service_bytes_recursive");
      if (blk != null && blk.Value.ValueKind == JsonValueKind.Array)
      {
        foreach (var entry in blk.Value.EnumerateArray())
        {
          var op = entry.GetStringOrDefault("op")?.ToLowerInvariant();
          var val = entry.GetInt64OrDefault("value");
          if (op == "read")
            stats.BlockReadBytes += val;
          else if (op == "write")
            stats.BlockWriteBytes += val;
        }
      }

      return stats;
    }

    private static int GetPidsCount(JsonElement json)
    {
      var pidsStats = json.Prop("pids_stats");
      if (pidsStats == null || pidsStats.Value.ValueKind != JsonValueKind.Object)
        return 0;
      return pidsStats.Value.GetInt32OrDefault("current");
    }

    private static double CalculateCpuPercent(JsonElement cpuStats, JsonElement preCpu)
    {
      var cpuUsage = cpuStats.Prop("cpu_usage");
      var preCpuUsage = preCpu.Prop("cpu_usage");
      var cpuDelta =
          (cpuUsage?.GetInt64OrDefault("total_usage") ?? 0) -
          (preCpuUsage?.GetInt64OrDefault("total_usage") ?? 0);
      var systemDelta =
          cpuStats.GetInt64OrDefault("system_cpu_usage") -
          preCpu.GetInt64OrDefault("system_cpu_usage");

      if (systemDelta <= 0 || cpuDelta <= 0)
        return 0.0;

      var onlineCpus = cpuStats.GetInt32OrDefault("online_cpus", 1);
      return (double)cpuDelta / systemDelta * onlineCpus * 100.0;
    }

    #endregion
  }
}
