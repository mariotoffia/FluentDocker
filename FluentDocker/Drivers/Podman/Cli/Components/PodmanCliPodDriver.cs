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
  /// Podman CLI implementation of pod operations.
  /// </summary>
  public class PodmanCliPodDriver : PodmanCliDriverBase, IPodmanPodDriver
  {
    public PodmanCliPodDriver(IPodmanBinaryResolver binaryResolver) : base(binaryResolver)
    {
    }

    #region Lifecycle

    /// <inheritdoc />
    public async Task<CommandResponse<PodCreateResult>> CreatePodAsync(
        DriverContext context, PodCreateConfig config,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = BuildCreateArgs(config);
        var result = await ExecuteCommandAsync(args, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
          return CommandResponse<PodCreateResult>.Fail(
              result.Error ?? "Pod create failed",
              ErrorCodes.Pod.CreateFailed, result.ExitCode);

        return CommandResponse<PodCreateResult>.Ok(new PodCreateResult
        {
          Id = result.Output?.Trim()
        });
      }
      catch (Exception ex)
      {
        return CommandResponse<PodCreateResult>.Fail(
            ex.Message, ErrorCodes.Pod.CreateFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> StartPodAsync(
        DriverContext context, string name,
        CancellationToken cancellationToken = default)
    {
      return await ExecuteSimplePodCommandAsync(
          $"pod start {name}", ErrorCodes.Pod.StartFailed, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> StopPodAsync(
        DriverContext context, string name, int? timeout = null,
        CancellationToken cancellationToken = default)
    {
      var args = timeout.HasValue
          ? $"pod stop -t {timeout.Value} {name}"
          : $"pod stop {name}";

      return await ExecuteSimplePodCommandAsync(
          args, ErrorCodes.Pod.StopFailed, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> RestartPodAsync(
        DriverContext context, string name, int? timeout = null,
        CancellationToken cancellationToken = default)
    {
      var args = timeout.HasValue
          ? $"pod restart -t {timeout.Value} {name}"
          : $"pod restart {name}";

      return await ExecuteSimplePodCommandAsync(
          args, ErrorCodes.Pod.RestartFailed, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> KillPodAsync(
        DriverContext context, string name, string signal = null,
        CancellationToken cancellationToken = default)
    {
      var args = !string.IsNullOrEmpty(signal)
          ? $"pod kill --signal {signal} {name}"
          : $"pod kill {name}";

      return await ExecuteSimplePodCommandAsync(
          args, ErrorCodes.Pod.KillFailed, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> PausePodAsync(
        DriverContext context, string name,
        CancellationToken cancellationToken = default)
    {
      return await ExecuteSimplePodCommandAsync(
          $"pod pause {name}", ErrorCodes.Pod.PauseFailed, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> UnpausePodAsync(
        DriverContext context, string name,
        CancellationToken cancellationToken = default)
    {
      return await ExecuteSimplePodCommandAsync(
          $"pod unpause {name}", ErrorCodes.Pod.UnpauseFailed, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> RemovePodAsync(
        DriverContext context, string name, bool force = false,
        CancellationToken cancellationToken = default)
    {
      var args = force ? $"pod rm -f {name}" : $"pod rm {name}";

      return await ExecuteSimplePodCommandAsync(
          args, ErrorCodes.Pod.RemoveFailed, cancellationToken);
    }

    #endregion

    #region Query

    /// <inheritdoc />
    public async Task<CommandResponse<IList<PodInfo>>> ListPodsAsync(
        DriverContext context,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteCommandAsync(
            "pod ps --format json", cancellationToken);

        if (!result.Success)
          return CommandResponse<IList<PodInfo>>.Fail(
              result.Error ?? "Pod list failed",
              ErrorCodes.Pod.ListFailed, result.ExitCode);

        var pods = ParsePodList(result.Output);
        return CommandResponse<IList<PodInfo>>.Ok(pods);
      }
      catch (Exception ex)
      {
        return CommandResponse<IList<PodInfo>>.Fail(
            ex.Message, ErrorCodes.Pod.ListFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<PodInspectResult>> InspectPodAsync(
        DriverContext context, string name,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteCommandAsync(
            $"pod inspect {name}", cancellationToken);

        if (!result.Success)
          return CommandResponse<PodInspectResult>.Fail(
              result.Error ?? "Pod inspect failed",
              ErrorCodes.Pod.InspectFailed, result.ExitCode);

        var inspect = ParsePodInspect(result.Output);
        return CommandResponse<PodInspectResult>.Ok(inspect);
      }
      catch (Exception ex)
      {
        return CommandResponse<PodInspectResult>.Fail(
            ex.Message, ErrorCodes.Pod.InspectFailed);
      }
    }

    #endregion

    #region Argument Building

    private static string BuildCreateArgs(PodCreateConfig config)
    {
      var args = "pod create";

      if (!string.IsNullOrEmpty(config.Name))
        args += $" --name {config.Name}";
      if (!string.IsNullOrEmpty(config.Hostname))
        args += $" --hostname {config.Hostname}";
      if (!string.IsNullOrEmpty(config.Network))
        args += $" --network {config.Network}";
      if (!string.IsNullOrEmpty(config.InfraImage))
        args += $" --infra-image {config.InfraImage}";
      if (!string.IsNullOrEmpty(config.Share))
        args += $" --share {config.Share}";

      foreach (var label in config.Labels)
        args += $" --label {label.Key}={label.Value}";
      foreach (var dns in config.Dns)
        args += $" --dns {dns}";
      foreach (var port in config.Ports)
        args += $" -p {port}";

      return args;
    }

    #endregion

    #region JSON Parsing

    private static List<PodInfo> ParsePodList(string json)
    {
      var pods = new List<PodInfo>();
      if (string.IsNullOrWhiteSpace(json))
        return pods;

      try
      {
        var trimmed = json.Trim();
        if (trimmed.StartsWith('['))
        {
          var root = JsonHelper.ParseElement(trimmed);
          foreach (var token in root.EnumerateArraySafe())
            pods.Add(ParsePodInfoFromToken(token));
        }
        else
        {
          foreach (var line in trimmed.Split('\n',
              StringSplitOptions.RemoveEmptyEntries))
            pods.Add(ParsePodInfoFromToken(JsonHelper.ParseElement(line.Trim())));
        }
      }
      catch { /* Return partial results */ }

      return pods;
    }

    private static PodInfo ParsePodInfoFromToken(JsonElement token)
    {
      var info = new PodInfo
      {
        Id = token.GetStringOrDefault("Id", "id"),
        Name = token.GetStringOrDefault("Name", "name"),
        Status = token.GetStringOrDefault("Status", "status"),
        Created = token.GetStringOrDefault("Created", "created"),
        InfraId = token.GetStringOrDefault("InfraId", "infraId")
      };

      var numProp = token.Prop("NumContainers", "num_containers");
      if (numProp.HasValue && numProp.Value.ValueKind == JsonValueKind.Number)
        info.NumContainers = numProp.Value.GetInt32();

      var containers = token.Prop("Containers", "containers");
      if (containers.HasValue && containers.Value.ValueKind == JsonValueKind.Array)
      {
        foreach (var c in containers.Value.EnumerateArray())
          info.Containers.Add(ParsePodContainerInfoFromToken(c));
      }

      return info;
    }

    private static PodInspectResult ParsePodInspect(string json)
    {
      var result = new PodInspectResult();
      if (string.IsNullOrWhiteSpace(json))
        return result;

      try
      {
        var trimmed = json.Trim();
        JsonElement obj;
        if (trimmed.StartsWith('['))
        {
          var root = JsonHelper.ParseElement(trimmed);
          obj = root.EnumerateArray().First();
        }
        else
        {
          obj = JsonHelper.ParseElement(trimmed);
        }

        result.Id = obj.GetStringOrDefault("Id", "id");
        result.Name = obj.GetStringOrDefault("Name", "name");
        result.Created = obj.GetStringOrDefault("Created", "created");
        result.Hostname = obj.GetStringOrDefault("Hostname", "hostname");

        var state = obj.Prop("State", "state");
        if (state.HasValue)
          result.State = state.Value.ValueKind == JsonValueKind.String
              ? state.Value.GetString()
              : state.Value.GetRawText();

        result.InfraContainerId =
            obj.GetStringOrDefault("InfraContainerId", "infraContainerId");

        var numProp = obj.Prop("NumContainers", "num_containers");
        if (numProp.HasValue && numProp.Value.ValueKind == JsonValueKind.Number)
          result.NumContainers = numProp.Value.GetInt32();

        var containers = obj.Prop("Containers", "containers");
        if (containers.HasValue && containers.Value.ValueKind == JsonValueKind.Array)
        {
          foreach (var c in containers.Value.EnumerateArray())
            result.Containers.Add(ParsePodContainerInfoFromToken(c));
        }
      }
      catch { /* Return partial results */ }

      return result;
    }

    private static PodContainerInfo ParsePodContainerInfoFromToken(JsonElement token)
    {
      return new PodContainerInfo
      {
        Id = token.GetStringOrDefault("Id", "id"),
        Name = token.GetStringOrDefault("Name", "name"),
        State = token.GetStringOrDefault("State", "state")
      };
    }

    #endregion

    #region Helpers

    private async Task<CommandResponse<Unit>> ExecuteSimplePodCommandAsync(
        string args, string errorCode, CancellationToken cancellationToken)
    {
      try
      {
        var result = await ExecuteCommandAsync(args, cancellationToken).ConfigureAwait(false);
        return result.Success
            ? CommandResponse<Unit>.Ok(Unit.Default)
            : CommandResponse<Unit>.Fail(
                result.Error ?? $"Pod command failed: {args}",
                errorCode, result.ExitCode);
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, errorCode);
      }
    }

    #endregion
  }
}
