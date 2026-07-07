using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Drivers.Podman.Cli.Components
{
  public partial class PodmanCliMachineDriver
  {
    #region Query

    /// <inheritdoc />
    public async Task<CommandResponse<IList<MachineInfo>>> ListAsync(
        DriverContext context,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteCommandAsync(
            context,
            "machine list --format json", cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<IList<MachineInfo>>.Fail(
              ErrorOrDefault(result, "Machine list failed"), FailureCode(result.Error, ErrorCodes.Machine.ListFailed),
              CreateErrorContext(context, "ListMachines", result), result.ExitCode);

        var machines = ParseMachineList(result.Output);
        return CommandResponse<IList<MachineInfo>>.Ok(machines);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<IList<MachineInfo>>.Fail(
            ex.Message, FailureCode(ex, ErrorCodes.Machine.ListFailed));
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<MachineInspectResult>> InspectAsync(
        DriverContext context, string name = null,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = string.IsNullOrEmpty(name)
            ? "machine inspect"
            : $"machine inspect {QuotePositionalArgument(name, nameof(name))}";
        var result = await ExecuteCommandAsync(context, args, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<MachineInspectResult>.Fail(
              ErrorOrDefault(result, "Machine inspect failed"), FailureCode(result.Error, ErrorCodes.Machine.InspectFailed),
              CreateErrorContext(context, "InspectMachine", result), result.ExitCode);

        var inspect = ParseMachineInspect(result.Output);
        return CommandResponse<MachineInspectResult>.Ok(inspect);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<MachineInspectResult>.Fail(
            ex.Message, FailureCode(ex, ErrorCodes.Machine.InspectFailed));
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<MachineHostInfo>> InfoAsync(
        DriverContext context,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteCommandAsync(
            context,
            "machine info --format json", cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<MachineHostInfo>.Fail(
              ErrorOrDefault(result, "Machine info failed"), FailureCode(result.Error, ErrorCodes.Machine.InfoFailed),
              CreateErrorContext(context, "MachineInfo", result), result.ExitCode);

        var info = ParseMachineInfo(result.Output);
        return CommandResponse<MachineHostInfo>.Ok(info);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<MachineHostInfo>.Fail(
            ex.Message, FailureCode(ex, ErrorCodes.Machine.InfoFailed));
      }
    }

    #endregion

    #region JSON Parsing

    internal static IList<MachineInfo> ParseMachineList(string json)
    {
      var machines = new List<MachineInfo>();
      if (string.IsNullOrWhiteSpace(json))
        return machines;

      try
      {
        var trimmed = json.Trim();
        if (trimmed.StartsWith('['))
        {
          var root = JsonHelper.ParseElement(trimmed);
          foreach (var token in root.EnumerateArraySafe())
            machines.Add(ParseMachineInfoFromToken(token));
        }
        else
        {
          foreach (var line in trimmed.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            machines.Add(ParseMachineInfoFromToken(JsonHelper.ParseElement(line.Trim())));
        }
      }
      catch (Exception ex)
      {
        throw new FluentDockerException(
            $"Failed to parse Podman machine list output: {ex.Message}");
      }

      return machines;
    }

    private static MachineInfo ParseMachineInfoFromToken(JsonElement token)
    {
      var info = new MachineInfo
      {
        Name = token.GetStringOrDefault("Name", "name"),
        VMType = token.GetStringOrDefault("VMType")
                 ?? token.GetStringOrDefault("vmType")
                 ?? token.GetStringOrDefault("vmtype"),
        Created = token.GetStringOrDefault("Created", "created"),
        LastUp = token.GetStringOrDefault("LastUp")
                 ?? token.GetStringOrDefault("lastUp")
                 ?? token.GetStringOrDefault("lastup"),
        Default = token.GetBoolOrDefault("Default") || token.GetBoolOrDefault("default"),
        Running = token.GetBoolOrDefault("Running") || token.GetBoolOrDefault("running"),
        Starting = token.GetBoolOrDefault("Starting") || token.GetBoolOrDefault("starting")
      };

      var cpusProp = token.Prop("CPUs", "cpus") ?? token.Prop("Cpus");
      if (cpusProp.HasValue && cpusProp.Value.ValueKind == JsonValueKind.Number)
        info.Cpus = cpusProp.Value.GetInt32();

      var memProp = token.Prop("Memory", "memory");
      if (memProp.HasValue)
        info.Memory = ParseLongValue(memProp.Value);

      var diskProp = token.Prop("DiskSize", "diskSize") ?? token.Prop("disk_size");
      if (diskProp.HasValue)
        info.DiskSize = ParseLongValue(diskProp.Value);

      return info;
    }

    internal static MachineInspectResult ParseMachineInspect(string json)
    {
      var result = new MachineInspectResult();
      if (string.IsNullOrWhiteSpace(json))
        return result;

      try
      {
        var trimmed = json.Trim();
        JsonElement obj;
        if (trimmed.StartsWith('['))
        {
          var root = JsonHelper.ParseElement(trimmed);
          using var enumerator = root.EnumerateArray();
          if (!enumerator.MoveNext())
            return result;
          obj = enumerator.Current;
        }
        else
        {
          obj = JsonHelper.ParseElement(trimmed);
        }

        result.Name = obj.GetStringOrDefault("Name", "name");
        result.Created = obj.GetStringOrDefault("Created", "created");
        result.LastUp = obj.GetStringOrDefault("LastUp", "lastUp");

        var state = obj.Prop("State", "state");
        if (state.HasValue)
          result.State = state.Value.ValueKind == JsonValueKind.String
              ? state.Value.GetString()
              : state.Value.GetRawText();

        result.Rootful = obj.GetBoolOrDefault("Rootful") || obj.GetBoolOrDefault("rootful");

        var configDir = obj.Prop("ConfigDir", "configDir");
        if (configDir.HasValue)
        {
          if (configDir.Value.ValueKind == JsonValueKind.Object)
            result.ConfigDir = configDir.Value.GetStringOrDefault("Path")
                               ?? configDir.Value.GetStringOrDefault("path");
          else
            result.ConfigDir = configDir.Value.GetStringValue();
        }

        var resources = obj.Prop("Resources", "resources");
        if (resources.HasValue && resources.Value.ValueKind == JsonValueKind.Object)
        {
          var res = resources.Value;
          var memory = res.Prop("Memory", "memory");
          var diskSize = res.Prop("DiskSize", "diskSize");
          result.Resources = new MachineResources
          {
            Cpus = res.GetInt32OrDefault("CPUs",
                     res.GetInt32OrDefault("cpus", res.GetInt32OrDefault("Cpus"))),
            Memory = memory.HasValue ? ToBytes(ParseLongValue(memory.Value), 1024L * 1024L) : 0,
            DiskSize = diskSize.HasValue ? ToBytes(ParseLongValue(diskSize.Value), 1024L * 1024L * 1024L) : 0
          };
        }

        var connInfo = obj.Prop("ConnectionInfo", "connectionInfo");
        if (connInfo.HasValue && connInfo.Value.ValueKind == JsonValueKind.Object)
        {
          var ci = connInfo.Value;
          var socket = ci.Prop("PodmanSocket", "podmanSocket");
          if (socket.HasValue && socket.Value.ValueKind == JsonValueKind.Object)
          {
            result.ConnectionInfo = new MachineConnectionInfo
            {
              PodmanSocketPath = socket.Value.GetStringOrDefault("Path")
                                 ?? socket.Value.GetStringOrDefault("path")
            };
          }
        }
      }
      catch (Exception ex)
      {
        throw new FluentDockerException(
            $"Failed to parse Podman machine inspect output: {ex.Message}");
      }

      return result;
    }

    internal static MachineHostInfo ParseMachineInfo(string json)
    {
      var result = new MachineHostInfo();
      if (string.IsNullOrWhiteSpace(json))
        return result;

      try
      {
        var obj = JsonHelper.ParseElement(json.Trim());

        var host = obj.Prop("Host", "host");
        if (host.HasValue && host.Value.ValueKind == JsonValueKind.Object)
        {
          var h = host.Value;
          result.Arch = h.GetStringOrDefault("Arch", "arch");
          result.OS = h.GetStringOrDefault("OS", "os");
          result.CurrentMachine = h.GetStringOrDefault("CurrentMachine", "currentMachine");
          result.VMType = h.GetStringOrDefault("VMType", "vmType");
          var numProp = h.Prop("NumberOfMachines", "numberOfMachines");
          if (numProp.HasValue && numProp.Value.ValueKind == JsonValueKind.Number)
            result.NumberOfMachines = numProp.Value.GetInt32();
          result.MachineConfigDir =
              h.GetStringOrDefault("MachineConfigDir", "machineConfigDir");
        }

        var version = obj.Prop("Version", "version");
        if (version.HasValue && version.Value.ValueKind == JsonValueKind.Object)
        {
          var v = version.Value;
          result.ApiVersion =
              v.GetStringOrDefault("APIVersion")
              ?? v.GetStringOrDefault("apiVersion")
              ?? v.GetStringOrDefault("ApiVersion");
          result.Version =
              v.GetStringOrDefault("Version")
              ?? v.GetStringOrDefault("version");
        }
      }
      catch (Exception ex)
      {
        throw new FluentDockerException(
            $"Failed to parse Podman machine info output: {ex.Message}");
      }

      return result;
    }

    private static long ParseLongValue(JsonElement token)
    {
      if (token.ValueKind == JsonValueKind.String)
        return long.TryParse(token.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
      if (token.ValueKind == JsonValueKind.Number)
        return token.TryGetInt64(out var lv) ? lv : 0;
      return 0;
    }

    private static long ToBytes(long value, long multiplier) => value * multiplier;

    #endregion
  }
}
