using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers.Podman.Cli.Binary;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Drivers.Podman.Cli.Components
{
  /// <summary>
  /// Podman CLI implementation of machine (VM) management operations.
  /// </summary>
  public class PodmanCliMachineDriver(IPodmanBinaryResolver binaryResolver) : PodmanCliDriverBase(binaryResolver), IPodmanMachineDriver
  {

    #region Lifecycle

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> InitAsync(
        DriverContext context, MachineInitConfig config,
        CancellationToken cancellationToken = default)
    {
      ArgumentNullException.ThrowIfNull(config);

      var args = BuildInitArgs(config);
      return await ExecuteSimpleMachineCommandAsync(
          args, ErrorCodes.Machine.InitFailed, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> StartAsync(
        DriverContext context, string name = null,
        CancellationToken cancellationToken = default)
    {
      var args = string.IsNullOrEmpty(name) ? "machine start" : $"machine start {name}";
      return await ExecuteSimpleMachineCommandAsync(
          args, ErrorCodes.Machine.StartFailed, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> StopAsync(
        DriverContext context, string name = null,
        CancellationToken cancellationToken = default)
    {
      var args = string.IsNullOrEmpty(name) ? "machine stop" : $"machine stop {name}";
      return await ExecuteSimpleMachineCommandAsync(
          args, ErrorCodes.Machine.StopFailed, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> RemoveAsync(
        DriverContext context, string name = null, bool force = false,
        CancellationToken cancellationToken = default)
    {
      var args = "machine rm";
      if (force)
        args += " -f";
      if (!string.IsNullOrEmpty(name))
        args += $" {name}";

      return await ExecuteSimpleMachineCommandAsync(
          args, ErrorCodes.Machine.RemoveFailed, cancellationToken);
    }

    #endregion

    #region Query

    /// <inheritdoc />
    public async Task<CommandResponse<IList<MachineInfo>>> ListAsync(
        DriverContext context,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteCommandAsync(
            "machine list --format json", cancellationToken);

        if (!result.Success)
          return CommandResponse<IList<MachineInfo>>.Fail(
              result.Error ?? "Machine list failed",
              ErrorCodes.Machine.ListFailed, result.ExitCode);

        var machines = ParseMachineList(result.Output);
        return CommandResponse<IList<MachineInfo>>.Ok(machines);
      }
      catch (Exception ex)
      {
        return CommandResponse<IList<MachineInfo>>.Fail(
            ex.Message, ErrorCodes.Machine.ListFailed);
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
            : $"machine inspect {name}";

        var result = await ExecuteCommandAsync(args, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
          return CommandResponse<MachineInspectResult>.Fail(
              result.Error ?? "Machine inspect failed",
              ErrorCodes.Machine.InspectFailed, result.ExitCode);

        var inspect = ParseMachineInspect(result.Output);
        return CommandResponse<MachineInspectResult>.Ok(inspect);
      }
      catch (Exception ex)
      {
        return CommandResponse<MachineInspectResult>.Fail(
            ex.Message, ErrorCodes.Machine.InspectFailed);
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
            "machine info --format json", cancellationToken);

        if (!result.Success)
          return CommandResponse<MachineHostInfo>.Fail(
              result.Error ?? "Machine info failed",
              ErrorCodes.Machine.InfoFailed, result.ExitCode);

        var info = ParseMachineInfo(result.Output);
        return CommandResponse<MachineHostInfo>.Ok(info);
      }
      catch (Exception ex)
      {
        return CommandResponse<MachineHostInfo>.Fail(
            ex.Message, ErrorCodes.Machine.InfoFailed);
      }
    }

    #endregion

    #region Interaction

    /// <inheritdoc />
    public async Task<CommandResponse<string>> SshAsync(
        DriverContext context, string name = null, string command = null,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = "machine ssh";
        if (!string.IsNullOrEmpty(name))
          args += $" {name}";
        if (!string.IsNullOrEmpty(command))
          args += $" {command}";

        var result = await ExecuteCommandAsync(args, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
          return CommandResponse<string>.Fail(
              result.Error ?? "Machine SSH failed",
              ErrorCodes.Machine.SshFailed, result.ExitCode);

        return CommandResponse<string>.Ok(result.Output?.TrimEnd());
      }
      catch (Exception ex)
      {
        return CommandResponse<string>.Fail(
            ex.Message, ErrorCodes.Machine.SshFailed);
      }
    }

    #endregion

    #region Configuration

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> SetAsync(
        DriverContext context, MachineSetConfig config, string name = null,
        CancellationToken cancellationToken = default)
    {
      ArgumentNullException.ThrowIfNull(config);

      var args = BuildSetArgs(config, name);
      return await ExecuteSimpleMachineCommandAsync(
          args, ErrorCodes.Machine.SetFailed, cancellationToken);
    }

    #endregion

    #region Argument Building

    internal static string BuildInitArgs(MachineInitConfig config)
    {
      var args = "machine init";

      if (config.Cpus.HasValue)
        args += $" --cpus {config.Cpus.Value}";
      if (config.DiskSizeGiB.HasValue)
        args += $" --disk-size {config.DiskSizeGiB.Value}";
      if (config.MemoryMiB.HasValue)
        args += $" --memory {config.MemoryMiB.Value}";
      if (config.Rootful)
        args += " --rootful";
      if (!string.IsNullOrEmpty(config.Image))
        args += $" --image {config.Image}";
      if (!string.IsNullOrEmpty(config.Username))
        args += $" --username {config.Username}";
      if (config.Now)
        args += " --now";

      foreach (var vol in config.Volumes)
        args += $" -v {vol}";

      if (!string.IsNullOrEmpty(config.Name))
        args += $" {config.Name}";

      return args;
    }

    internal static string BuildSetArgs(MachineSetConfig config, string name = null)
    {
      var args = "machine set";

      if (config.Cpus.HasValue)
        args += $" --cpus {config.Cpus.Value}";
      if (config.DiskSizeGiB.HasValue)
        args += $" --disk-size {config.DiskSizeGiB.Value}";
      if (config.MemoryMiB.HasValue)
        args += $" --memory {config.MemoryMiB.Value}";
      if (config.Rootful.HasValue)
        args += config.Rootful.Value ? " --rootful" : " --rootful=false";

      if (!string.IsNullOrEmpty(name))
        args += $" {name}";

      return args;
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
          foreach (var line in trimmed.Split('\n',
              StringSplitOptions.RemoveEmptyEntries))
            machines.Add(ParseMachineInfoFromToken(JsonHelper.ParseElement(line.Trim())));
        }
      }
      catch (Exception ex)
      {
        throw new InvalidOperationException(
            $"Failed to parse Podman machine list JSON: {ex.Message}", ex);
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
        Running = token.GetBoolOrDefault("Running") || token.GetBoolOrDefault("running")
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
          result.Resources = new MachineResources
          {
            Cpus = res.GetInt32OrDefault("CPUs",
                     res.GetInt32OrDefault("cpus", res.GetInt32OrDefault("Cpus"))),
            MemoryMiB = res.GetInt32OrDefault("Memory", res.GetInt32OrDefault("memory")),
            DiskSizeGiB = res.GetInt32OrDefault("DiskSize", res.GetInt32OrDefault("diskSize"))
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
        throw new InvalidOperationException(
            $"Failed to parse Podman machine inspect JSON: {ex.Message}", ex);
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
        throw new InvalidOperationException(
            $"Failed to parse Podman machine info JSON: {ex.Message}", ex);
      }

      return result;
    }

    private static long ParseLongValue(JsonElement token)
    {
      if (token.ValueKind == JsonValueKind.String)
        return long.TryParse(token.GetString(), out var v) ? v : 0;
      if (token.ValueKind == JsonValueKind.Number)
        return token.TryGetInt64(out var lv) ? lv : 0;
      return 0;
    }

    #endregion

    #region Helpers

    private async Task<CommandResponse<Unit>> ExecuteSimpleMachineCommandAsync(
        string args, string errorCode, CancellationToken cancellationToken)
    {
      try
      {
        var result = await ExecuteCommandAsync(args, cancellationToken).ConfigureAwait(false);
        return result.Success
            ? CommandResponse<Unit>.Ok(Unit.Default)
            : CommandResponse<Unit>.Fail(
                result.Error ?? $"Machine command failed: {args}",
                errorCode, result.ExitCode);
      }
      catch (Exception ex) when (ex is not ArgumentNullException)
      {
        return CommandResponse<Unit>.Fail(ex.Message, errorCode);
      }
    }

    #endregion
  }
}
