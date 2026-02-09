using System;
using System.Collections.Generic;
using System.Linq;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Api.ApiModels;
using FluentDocker.Model.Containers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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

      if (duration.EndsWith("s") &&
          long.TryParse(duration[..^1], out var sec))
        return sec * 1_000_000_000;

      if (duration.EndsWith("m") &&
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

      return dict.Count > 0 ? JsonConvert.SerializeObject(dict) : null;
    }

    #endregion

    #region JSON Parsing

    private static Container ParseContainerInspect(JObject json)
    {
      if (json == null)
        return new Container();
      return new Container
      {
        Id = json.Value<string>("Id"),
        Name = json.Value<string>("Name")?.TrimStart('/'),
        Image = json.Value<string>("Image"),
        Created = json.Value<DateTime?>("Created") ?? DateTime.MinValue,
        Driver = json.Value<string>("Driver"),
        State = ParseContainerState(json["State"]),
        Config = ParseContainerConfig(json["Config"]),
        NetworkSettings = ParseNetworkSettings(json["NetworkSettings"])
      };
    }

    private static ContainerState ParseContainerState(JToken token)
    {
      if (token == null)
        return new ContainerState();
      return new ContainerState
      {
        Status = token.Value<string>("Status"),
        Running = token.Value<bool?>("Running") ?? false,
        Paused = token.Value<bool?>("Paused") ?? false,
        Restarting = token.Value<bool?>("Restarting") ?? false,
        OOMKilled = token.Value<bool?>("OOMKilled") ?? false,
        Dead = token.Value<bool?>("Dead") ?? false,
        Pid = token.Value<int?>("Pid") ?? 0,
        ExitCode = token.Value<long?>("ExitCode") ?? 0,
        Error = token.Value<string>("Error"),
        StartedAt = token.Value<DateTime?>("StartedAt") ?? DateTime.MinValue,
        FinishedAt = token.Value<DateTime?>("FinishedAt") ?? DateTime.MinValue
      };
    }

    private static ContainerConfig ParseContainerConfig(JToken token)
    {
      if (token == null)
        return null;
      return new ContainerConfig
      {
        Hostname = token.Value<string>("Hostname"),
        User = token.Value<string>("User"),
        Tty = token.Value<bool?>("Tty") ?? false,
        OpenStdin = token.Value<bool?>("OpenStdin") ?? false,
        Image = token.Value<string>("Image"),
        WorkingDir = token.Value<string>("WorkingDir"),
        Cmd = token["Cmd"]?.ToObject<string[]>(),
        EntryPoint = token["Entrypoint"]?.ToObject<string[]>(),
        Env = token["Env"]?.ToObject<string[]>(),
        Labels = token["Labels"]?.ToObject<Dictionary<string, string>>(),
        StopSignal = token.Value<string>("StopSignal")
      };
    }

    private static ContainerNetworkSettings ParseNetworkSettings(JToken token)
    {
      if (token == null)
        return null;
      return new ContainerNetworkSettings
      {
        Bridge = token.Value<string>("Bridge"),
        Gateway = token.Value<string>("Gateway"),
        IPAddress = token.Value<string>("IPAddress"),
        MacAddress = token.Value<string>("MacAddress"),
        Networks = token["Networks"]
              ?.ToObject<Dictionary<string, BridgeNetwork>>()
      };
    }

    private static IList<Container> ParseContainerList(JArray array)
    {
      var containers = new List<Container>();
      if (array == null)
        return containers;

      foreach (var token in array)
      {
        containers.Add(new Container
        {
          Id = token.Value<string>("Id"),
          Image = token.Value<string>("Image"),
          Created = DateTimeOffset
                .FromUnixTimeSeconds(token.Value<long?>("Created") ?? 0)
                .UtcDateTime,
          Name = (token["Names"] as JArray)?
                .FirstOrDefault()?.Value<string>()?.TrimStart('/'),
          State = new ContainerState
          {
            Status = token.Value<string>("State")
          }
        });
      }

      return containers;
    }

    private static ContainerStatsResult ParseContainerStats(
        JObject json, string containerId)
    {
      if (json == null)
        return new ContainerStatsResult { ContainerId = containerId };

      var stats = new ContainerStatsResult
      {
        ContainerId = containerId,
        Name = json.Value<string>("name")?.TrimStart('/'),
        Pids = json["pids_stats"]?.Value<int?>("current") ?? 0
      };

      var cpuStats = json["cpu_stats"];
      var preCpu = json["precpu_stats"];
      if (cpuStats != null && preCpu != null)
        stats.CpuPercent = CalculateCpuPercent(cpuStats, preCpu);

      var memStats = json["memory_stats"];
      if (memStats != null)
      {
        stats.MemoryUsage = memStats.Value<long?>("usage") ?? 0;
        stats.MemoryLimit = memStats.Value<long?>("limit") ?? 0;
        if (stats.MemoryLimit > 0)
          stats.MemoryPercent =
              (double)stats.MemoryUsage / stats.MemoryLimit * 100.0;
      }

      if (json["networks"] is JObject netObj)
      {
        foreach (var prop in netObj.Properties())
        {
          stats.NetworkRxBytes +=
              prop.Value.Value<long?>("rx_bytes") ?? 0;
          stats.NetworkTxBytes +=
              prop.Value.Value<long?>("tx_bytes") ?? 0;
        }
      }

      if (json["blkio_stats"]?["io_service_bytes_recursive"] is JArray blk)
      {
        foreach (var entry in blk)
        {
          var op = entry.Value<string>("op")?.ToLowerInvariant();
          var val = entry.Value<long?>("value") ?? 0;
          if (op == "read")
            stats.BlockReadBytes += val;
          else if (op == "write")
            stats.BlockWriteBytes += val;
        }
      }

      return stats;
    }

    private static double CalculateCpuPercent(JToken cpuStats, JToken preCpu)
    {
      var cpuDelta =
          (cpuStats["cpu_usage"]?.Value<long?>("total_usage") ?? 0) -
          (preCpu["cpu_usage"]?.Value<long?>("total_usage") ?? 0);
      var systemDelta =
          (cpuStats.Value<long?>("system_cpu_usage") ?? 0) -
          (preCpu.Value<long?>("system_cpu_usage") ?? 0);

      if (systemDelta <= 0 || cpuDelta <= 0)
        return 0.0;

      var onlineCpus = cpuStats.Value<int?>("online_cpus") ?? 1;
      return (double)cpuDelta / systemDelta * onlineCpus * 100.0;
    }

    #endregion
  }
}
