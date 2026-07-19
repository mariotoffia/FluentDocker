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
  /// Split across partials to stay under the 500-line file cap: this file holds the
  /// container/state/health/config/mounts parsing and the shared low-level helpers;
  /// <c>PodmanContainerParser.Network.cs</c> holds the <c>NetworkSettings</c> cluster.
  /// </summary>
  public static partial class PodmanContainerParser
  {
    #region JSON Parsing

    /// <summary>
    /// Parses <c>podman ps --format json</c> output into containers. Accepts both the JSON-array
    /// form and newline-delimited JSON objects (one object per line), since podman emits either
    /// depending on version/output size.
    /// </summary>
    /// <param name="json">Raw stdout from <c>podman ps --format json</c>.</param>
    /// <returns>
    /// One <see cref="Container"/> per list entry; an empty (never null) list when
    /// <paramref name="json"/> is null, empty, or whitespace-only.
    /// </returns>
    /// <exception cref="JsonException">
    /// The array-form text (when it starts with '[') or an individual NDJSON line is not valid JSON.
    /// </exception>
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
      string? name = null;
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

      var state = token.GetStringOrDefault("State");
      var status = state ?? token.GetStringOrDefault("Status");
      return new Container
      {
        Id = token.GetStringOrDefault("Id") ?? token.GetStringOrDefault("ID"),
        Image = token.GetStringOrDefault("Image"),
        Name = name,
        Created = ParseDateTime(token.Prop("Created") ?? token.Prop("CreatedAt")),
        State = new ContainerState
        {
          Status = status,
          Running = IsRunningState(state, status, token.GetBoolOrDefault("Exited")),
          StartedAt = ParseDateTime(token.Prop("StartedAt") ?? token.Prop("Started"))
        }
      };
    }

    private static bool IsRunningState(string? state, string? status, bool exited)
    {
      if (!string.IsNullOrEmpty(state))
        return string.Equals(state, "running", StringComparison.OrdinalIgnoreCase);
      return !exited && !string.IsNullOrEmpty(status)
        && status.StartsWith("Up", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Parses <c>podman inspect &lt;container&gt;</c> output — a single JSON object, or a
    /// single-element JSON array (the shape podman inspect actually emits).
    /// </summary>
    /// <param name="json">Raw stdout from <c>podman inspect</c>. Must not be null.</param>
    /// <returns>
    /// The parsed <see cref="Container"/>, with nested state/config/mounts/network settings
    /// resolved via the corresponding <c>Parse*</c> methods below. An empty JSON array yields a
    /// default (all-null/empty) <see cref="Container"/> rather than throwing.
    /// </returns>
    /// <exception cref="NullReferenceException"><paramref name="json"/> is null.</exception>
    /// <exception cref="JsonException"><paramref name="json"/> is not valid JSON.</exception>
    public static Container ParseContainerInspect(string json)
    {
      var trimmed = json.Trim();
      JsonElement token;
      if (trimmed.StartsWith('['))
      {
        var root = JsonHelper.ParseElement(trimmed);
        using var enumerator = root.EnumerateArray();
        if (!enumerator.MoveNext())
          return new Container();
        token = enumerator.Current;
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

    /// <summary>
    /// Parses the <c>State</c> object of a container inspect payload, including its nested
    /// <c>Health</c>/<c>Healthcheck</c> block.
    /// </summary>
    /// <param name="stateToken">The <c>State</c> property value, or null if absent.</param>
    /// <returns>
    /// The parsed <see cref="ContainerState"/>; a default (all-false/null) empty instance when
    /// <paramref name="stateToken"/> is null or a JSON null/undefined token — never throws for
    /// those cases.
    /// </returns>
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

    /// <summary>
    /// Parses a container's <c>Health</c> (or podman's <c>Healthcheck</c> alias) block, including
    /// its <c>Log</c> array.
    /// </summary>
    /// <param name="healthToken">The <c>Health</c>/<c>Healthcheck</c> property value, or null.</param>
    /// <returns>
    /// The parsed <see cref="Health"/>; <c>null</c> when <paramref name="healthToken"/> is null or
    /// a JSON null/undefined token (a container without a configured healthcheck has no health
    /// object). <see cref="Health.Status"/> falls back to <see cref="HealthState.Unknown"/> when
    /// the <c>Status</c> string is missing, empty, or not a recognized <see cref="HealthState"/>
    /// name. <see cref="Health.Log"/> stays null unless the <c>Log</c> property is a JSON array.
    /// </returns>
    public static Health? ParseHealth(JsonElement? healthToken)
    {
      if (healthToken == null || healthToken.Value.IsNullOrUndefined())
        return null;

      var el = healthToken.Value;
      var statusStr = el.GetStringOrDefault("Status");
      HealthState status;
      if (string.IsNullOrEmpty(statusStr))
        status = HealthState.Unknown;
      else if (!Enum.TryParse(statusStr, ignoreCase: true, out status) || !Enum.IsDefined(status))
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

    /// <summary>
    /// Parses the <c>Config</c> object of a container inspect payload, including podman's
    /// <c>Cmd</c>/<c>Entrypoint</c> string-or-array quirk and the <c>Domainname</c>/<c>DomainName</c>
    /// casing variance.
    /// </summary>
    /// <param name="configToken">The <c>Config</c> property value, or null if absent.</param>
    /// <returns>
    /// The parsed <see cref="ContainerConfig"/>; <c>null</c> when <paramref name="configToken"/>
    /// is null or a JSON null/undefined token.
    /// </returns>
    public static ContainerConfig? ParseContainerConfig(JsonElement? configToken)
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
        StopSignal = ReadStringOrNumber(el.Prop("StopSignal")),
        Env = ParseStringArray(el.Prop("Env")),
        Cmd = ParseStringOrArray(el.Prop("Cmd")),
        EntryPoint = ParseStringOrArray(
              el.Prop("Entrypoint") ?? el.Prop("EntryPoint")),
        ExposedPorts = ParseExposedPorts(el.Prop("ExposedPorts")),
        Labels = ParseStringDictionary(el.Prop("Labels"))
      };
    }

    /// <summary>
    /// Parses the <c>Mounts</c> array of a container inspect payload.
    /// </summary>
    /// <param name="mountsToken">The <c>Mounts</c> property value, or null if absent.</param>
    /// <returns>
    /// One <see cref="ContainerMount"/> per array entry; an empty (never null) array when
    /// <paramref name="mountsToken"/> is null, not a JSON array, or an empty array.
    /// </returns>
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

    #endregion

    #region Parsing Helpers

    /// <summary>
    /// Parses a JsonElement that may be a JSON array of strings or a single string value.
    /// Handles the Podman quirk where fields like EntryPoint and Cmd can be either shape.
    /// </summary>
    /// <param name="token">The property value, or null if absent.</param>
    /// <returns>
    /// The array items when <paramref name="token"/> is a JSON array; a single-element array
    /// wrapping the value when it is a JSON string; otherwise <c>null</c> — including when
    /// <paramref name="token"/> is null, a JSON null/undefined token, or a non-string scalar
    /// (number/bool/object).
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="token"/> is an array containing a non-string, non-null element.
    /// </exception>
    public static string[]? ParseStringOrArray(JsonElement? token)
    {
      if (token == null || token.Value.IsNullOrUndefined())
        return null;

      var el = token.Value;
      if (el.ValueKind == JsonValueKind.Array)
      {
        var result = new List<string>();
        foreach (var item in el.EnumerateArray())
          result.Add(item.GetString()!);
        return [.. result];
      }

      var str = el.GetStringValue();
      return str != null ? [str] : null;
    }

    internal static string[]? ParseStringArray(JsonElement? token)
    {
      if (token == null || token.Value.ValueKind != JsonValueKind.Array)
        return null;

      var result = new List<string>();
      foreach (var item in token.Value.EnumerateArray())
        result.Add(item.GetString()!);
      return [.. result];
    }

    internal static IDictionary<string, string>? ParseStringDictionary(JsonElement? token)
    {
      if (token == null || token.Value.ValueKind != JsonValueKind.Object)
        return null;

      var dict = new Dictionary<string, string>();
      foreach (var prop in token.Value.EnumerateObject())
        dict[prop.Name] = prop.Value.GetString() ?? string.Empty;
      return dict;
    }

    internal static IDictionary<string, object>? ParseExposedPorts(JsonElement? token)
    {
      if (token == null || token.Value.ValueKind != JsonValueKind.Object)
        return null;

      var dict = new Dictionary<string, object>();
      foreach (var prop in token.Value.EnumerateObject())
        dict[prop.Name] = new { };
      return dict;
    }

    internal static DateTimeOffset ParseDateTime(JsonElement? token)
    {
      if (token == null || token.Value.IsNullOrUndefined())
        return DateTimeOffset.MinValue;

      if (token.Value.ValueKind == JsonValueKind.Number && token.Value.TryGetInt64(out var seconds))
        return DateTimeOffset.FromUnixTimeSeconds(seconds);

      var str = token.Value.GetStringValue();
      if (string.IsNullOrEmpty(str))
        return DateTimeOffset.MinValue;

      if (DateTimeOffset.TryParse(str, CultureInfo.InvariantCulture,
          DateTimeStyles.AssumeUniversal, out var dto))
        return dto;

      var lastSpace = str.LastIndexOf(' ');
      if (lastSpace > 0 && DateTimeOffset.TryParse(str[..lastSpace], CultureInfo.InvariantCulture,
          DateTimeStyles.AssumeUniversal, out dto))
        return dto;

      return DateTimeOffset.MinValue;
    }

    private static string? ReadStringOrNumber(JsonElement? token)
    {
      if (token == null || token.Value.IsNullOrUndefined())
        return null;
      if (token.Value.ValueKind == JsonValueKind.Number)
        return token.Value.ToString();
      return token.Value.GetStringValue();
    }

    #endregion
  }
}
