using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using FluentDocker.Common;
using FluentDocker.Model.Containers;
using Container = FluentDocker.Model.Containers.Container;
using ContainerState = FluentDocker.Model.Containers.ContainerState;

namespace FluentDocker.Drivers.Podman.Cli.Components
{
  /// <summary>
  /// Utility class for parsing Podman CLI JSON output into container model objects.
  /// Extracted from PodmanCliContainerDriver to separate parsing concerns from driver API.
  /// </summary>
  public static class PodmanContainerParser
  {
    #region JSON Parsing

    public static IList<Container> ParseContainerList(string json)
    {
      var containers = new List<Container>();
      if (string.IsNullOrWhiteSpace(json))
        return containers;

      var trimmed = json.Trim();
      if (trimmed.StartsWith('['))
      {
        var root = JsonHelper.ParseElement(trimmed);
        foreach (var token in root.EnumerateArraySafe())
          containers.Add(ParseContainerFromListToken(token));
      }
      else
      {
        foreach (var line in trimmed.Split('\n', StringSplitOptions.RemoveEmptyEntries))
          containers.Add(ParseContainerFromListToken(JsonHelper.ParseElement(line.Trim())));
      }

      return containers;
    }

    private static Container ParseContainerFromListToken(JsonElement token)
    {
      var names = token.Prop("Names") ?? token.Prop("Name");
      string name = null;
      if (names.HasValue && names.Value.ValueKind == JsonValueKind.Array)
      {
        var namesArr = names.Value;
        if (namesArr.GetArrayLength() > 0)
          name = namesArr[0].GetString();
      }
      else if (names.HasValue)
      {
        name = names.Value.GetStringValue();
      }

      return new Container
      {
        Id = token.GetStringOrDefault("Id") ?? token.GetStringOrDefault("ID"),
        Image = token.GetStringOrDefault("Image"),
        Name = name,
        State = new ContainerState
        {
          Status = token.GetStringOrDefault("State") ?? token.GetStringOrDefault("Status")
        }
      };
    }

    public static Container ParseContainerInspect(string json)
    {
      var trimmed = json.Trim();
      JsonElement token;
      if (trimmed.StartsWith('['))
      {
        var root = JsonHelper.ParseElement(trimmed);
        token = root.EnumerateArray().First();
      }
      else
      {
        token = JsonHelper.ParseElement(trimmed);
      }

      return new Container
      {
        Id = token.GetStringOrDefault("Id") ?? token.GetStringOrDefault("ID"),
        Image = token.GetStringOrDefault("Image"),
        Name = token.GetStringOrDefault("Name"),
        Created = ParseDateTime(token.Prop("Created")),
        ResolvConfPath = token.GetStringOrDefault("ResolvConfPath"),
        HostnamePath = token.GetStringOrDefault("HostnamePath"),
        HostsPath = token.GetStringOrDefault("HostsPath"),
        LogPath = token.GetStringOrDefault("LogPath"),
        RestartCount = token.GetInt32OrDefault("RestartCount"),
        Driver = token.GetStringOrDefault("Driver"),
        Args = ParseStringArray(token.Prop("Args")),
        State = ParseContainerState(token.Prop("State")),
        Config = ParseContainerConfig(token.Prop("Config")),
        Mounts = ParseMounts(token.Prop("Mounts")),
        NetworkSettings = ParseNetworkSettings(token.Prop("NetworkSettings"))
      };
    }

    public static ContainerState ParseContainerState(JsonElement? stateToken)
    {
      if (stateToken == null || stateToken.Value.IsNullOrUndefined())
        return new ContainerState();

      var el = stateToken.Value;
      return new ContainerState
      {
        Status = el.GetStringOrDefault("Status"),
        Running = el.GetBoolOrDefault("Running"),
        Paused = el.GetBoolOrDefault("Paused"),
        Restarting = el.GetBoolOrDefault("Restarting"),
        OOMKilled = el.GetBoolOrDefault("OOMKilled"),
        Dead = el.GetBoolOrDefault("Dead"),
        Pid = el.GetInt32OrDefault("Pid"),
        ExitCode = el.GetInt32OrDefault("ExitCode"),
        Error = el.GetStringOrDefault("Error"),
        StartedAt = ParseDateTime(el.Prop("StartedAt")),
        FinishedAt = ParseDateTime(el.Prop("FinishedAt")),
        Health = ParseHealth(el.Prop("Health") ?? el.Prop("Healthcheck"))
      };
    }

    public static Health ParseHealth(JsonElement? healthToken)
    {
      if (healthToken == null || healthToken.Value.IsNullOrUndefined())
        return null;

      var el = healthToken.Value;
      var statusStr = el.GetStringOrDefault("Status");
      HealthState status;
      if (string.IsNullOrEmpty(statusStr))
        status = HealthState.Unknown;
      else if (!Enum.TryParse(statusStr, ignoreCase: true, out status))
        status = HealthState.Unknown;

      var health = new Health
      {
        Status = status,
        FailingStreak = el.GetInt32OrDefault("FailingStreak")
      };

      var logProp = el.Prop("Log");
      if (logProp.HasValue && logProp.Value.ValueKind == JsonValueKind.Array)
      {
        health.Log = [];
        foreach (var entry in logProp.Value.EnumerateArray())
        {
          health.Log.Add(new HealthLog
          {
            Start = entry.GetStringOrDefault("Start"),
            End = entry.GetStringOrDefault("End"),
            ExitCode = entry.GetInt32OrDefault("ExitCode"),
            Output = entry.GetStringOrDefault("Output")
          });
        }
      }

      return health;
    }

    public static ContainerConfig ParseContainerConfig(JsonElement? configToken)
    {
      if (configToken == null || configToken.Value.IsNullOrUndefined())
        return null;

      var el = configToken.Value;
      return new ContainerConfig
      {
        Hostname = el.GetStringOrDefault("Hostname"),
        DomainName = el.GetStringOrDefault("DomainName")
                       ?? el.GetStringOrDefault("Domainname"),
        User = el.GetStringOrDefault("User"),
        AttachStdin = el.GetBoolOrDefault("AttachStdin"),
        AttachStdout = el.GetBoolOrDefault("AttachStdout"),
        AttachStderr = el.GetBoolOrDefault("AttachStderr"),
        Tty = el.GetBoolOrDefault("Tty"),
        OpenStdin = el.GetBoolOrDefault("OpenStdin"),
        StdinOnce = el.GetBoolOrDefault("StdinOnce"),
        Image = el.GetStringOrDefault("Image"),
        WorkingDir = el.GetStringOrDefault("WorkingDir"),
        StopSignal = el.GetStringOrDefault("StopSignal"),
        Env = ParseStringArray(el.Prop("Env")),
        Cmd = ParseStringOrArray(el.Prop("Cmd")),
        EntryPoint = ParseStringOrArray(
              el.Prop("Entrypoint") ?? el.Prop("EntryPoint")),
        ExposedPorts = ParseExposedPorts(el.Prop("ExposedPorts")),
        Labels = ParseStringDictionary(el.Prop("Labels"))
      };
    }

    public static ContainerMount[] ParseMounts(JsonElement? mountsToken)
    {
      if (mountsToken == null || mountsToken.Value.ValueKind != JsonValueKind.Array)
        return [];

      var mountsArray = mountsToken.Value;
      if (mountsArray.GetArrayLength() == 0)
        return [];

      var result = new List<ContainerMount>();
      foreach (var m in mountsArray.EnumerateArray())
      {
        result.Add(new ContainerMount
        {
          Name = m.GetStringOrDefault("Name"),
          Source = m.GetStringOrDefault("Source"),
          Destination = m.GetStringOrDefault("Destination"),
          Driver = m.GetStringOrDefault("Driver"),
          Mode = m.GetStringOrDefault("Mode"),
          RW = m.GetBoolOrDefault("RW"),
          Propagation = m.GetStringOrDefault("Propagation")
        });
      }
      return [.. result];
    }

    public static ContainerNetworkSettings ParseNetworkSettings(JsonElement? nsToken)
    {
      if (nsToken == null || nsToken.Value.IsNullOrUndefined())
        return null;

      var el = nsToken.Value;
      return new ContainerNetworkSettings
      {
        Bridge = el.GetStringOrDefault("Bridge"),
        SandboxID = el.GetStringOrDefault("SandboxID"),
        HairpinMode = el.GetBoolOrDefault("HairpinMode"),
        LinkLocalIPv6Address = el.GetStringOrDefault("LinkLocalIPv6Address"),
        LinkLocalIPv6PrefixLen = el.GetStringOrDefault("LinkLocalIPv6PrefixLen"),
        SandboxKey = el.GetStringOrDefault("SandboxKey"),
        SecondaryIPAddresses = el.GetStringOrDefault("SecondaryIPAddresses"),
        SecondaryIPv6Addresses = el.GetStringOrDefault("SecondaryIPv6Addresses"),
        EndpointID = el.GetStringOrDefault("EndpointID"),
        Gateway = el.GetStringOrDefault("Gateway"),
        GlobalIPv6Address = el.GetStringOrDefault("GlobalIPv6Address"),
        GlobalIPv6PrefixLen = el.GetStringOrDefault("GlobalIPv6PrefixLen"),
        IPAddress = el.GetStringOrDefault("IPAddress"),
        IPPrefixLen = el.GetStringOrDefault("IPPrefixLen"),
        IPv6Gateway = el.GetStringOrDefault("IPv6Gateway"),
        MacAddress = el.GetStringOrDefault("MacAddress"),
        Ports = ParsePorts(el.Prop("Ports")),
        Networks = ParseNetworks(el.Prop("Networks"))
      };
    }

    public static Dictionary<string, HostIpEndpoint[]> ParsePorts(JsonElement? portsToken)
    {
      if (portsToken == null || portsToken.Value.ValueKind != JsonValueKind.Object)
        return null;

      var result = new Dictionary<string, HostIpEndpoint[]>();
      foreach (var prop in portsToken.Value.EnumerateObject())
      {
        if (prop.Value.ValueKind == JsonValueKind.Array && prop.Value.GetArrayLength() > 0)
        {
          var bindings = new List<HostIpEndpoint>();
          foreach (var b in prop.Value.EnumerateArray())
          {
            bindings.Add(new HostIpEndpoint
            {
              HostIp = b.GetStringOrDefault("HostIp"),
              HostPort = b.GetStringOrDefault("HostPort")
            });
          }
          result[prop.Name] = [.. bindings];
        }
        else
        {
          result[prop.Name] = [];
        }
      }

      return result;
    }

    public static Dictionary<string, BridgeNetwork> ParseNetworks(JsonElement? networksToken)
    {
      if (networksToken == null || networksToken.Value.ValueKind != JsonValueKind.Object)
        return null;

      var result = new Dictionary<string, BridgeNetwork>();
      foreach (var prop in networksToken.Value.EnumerateObject())
      {
        var n = prop.Value;
        result[prop.Name] = new BridgeNetwork
        {
          NetworkID = n.GetStringOrDefault("NetworkID"),
          EndpointID = n.GetStringOrDefault("EndpointID"),
          Gateway = n.GetStringOrDefault("Gateway"),
          IPAddress = n.GetStringOrDefault("IPAddress"),
          IPPrefixLen = n.GetInt32OrDefault("IPPrefixLen"),
          IPv6Gateway = n.GetStringOrDefault("IPv6Gateway"),
          GlobalIPv6Address = n.GetStringOrDefault("GlobalIPv6Address"),
          GlobalIPv6PrefixLen = n.GetInt32OrDefault("GlobalIPv6PrefixLen"),
          MacAddress = n.GetStringOrDefault("MacAddress"),
          Aliases = ParseStringArray(n.Prop("Aliases"))
        };
      }

      return result;
    }

    #endregion

    #region Parsing Helpers

    /// <summary>
    /// Parses a JsonElement that may be a JSON array of strings or a single string value.
    /// Handles the Podman quirk where fields like EntryPoint and Cmd can be either format.
    /// </summary>
    public static string[] ParseStringOrArray(JsonElement? token)
    {
      if (token == null || token.Value.IsNullOrUndefined())
        return null;

      var el = token.Value;
      if (el.ValueKind == JsonValueKind.Array)
      {
        var result = new List<string>();
        foreach (var item in el.EnumerateArray())
          result.Add(item.GetString());
        return [.. result];
      }

      var str = el.GetStringValue();
      return str != null ? [str] : null;
    }

    internal static string[] ParseStringArray(JsonElement? token)
    {
      if (token == null || token.Value.ValueKind != JsonValueKind.Array)
        return null;

      var result = new List<string>();
      foreach (var item in token.Value.EnumerateArray())
        result.Add(item.GetString());
      return [.. result];
    }

    internal static IDictionary<string, string> ParseStringDictionary(JsonElement? token)
    {
      if (token == null || token.Value.ValueKind != JsonValueKind.Object)
        return null;

      var dict = new Dictionary<string, string>();
      foreach (var prop in token.Value.EnumerateObject())
        dict[prop.Name] = prop.Value.GetString();
      return dict;
    }

    internal static IDictionary<string, object> ParseExposedPorts(JsonElement? token)
    {
      if (token == null || token.Value.ValueKind != JsonValueKind.Object)
        return null;

      var dict = new Dictionary<string, object>();
      foreach (var prop in token.Value.EnumerateObject())
        dict[prop.Name] = new { };
      return dict;
    }

    internal static DateTime ParseDateTime(JsonElement? token)
    {
      if (token == null || token.Value.IsNullOrUndefined())
        return default;

      var str = token.Value.GetStringValue();
      if (string.IsNullOrEmpty(str))
        return default;

      return DateTimeOffset.TryParse(str, CultureInfo.InvariantCulture,
          DateTimeStyles.None, out var dto)
          ? dto.UtcDateTime
          : default;
    }

    #endregion
  }
}
