using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers.Podman.Cli.Binary;
using FluentDocker.Model.Drivers;
using Newtonsoft.Json.Linq;

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
        var result = await ExecuteCommandAsync(args, cancellationToken);

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

    private static IList<PodInfo> ParsePodList(string json)
    {
      var pods = new List<PodInfo>();
      if (string.IsNullOrWhiteSpace(json))
        return pods;

      try
      {
        var trimmed = json.Trim();
        if (trimmed.StartsWith("["))
        {
          var arr = JArray.Parse(trimmed);
          pods.AddRange(arr.Select(ParsePodInfoFromToken));
        }
        else
        {
          foreach (var line in trimmed.Split('\n',
              StringSplitOptions.RemoveEmptyEntries))
          {
            var obj = JObject.Parse(line.Trim());
            pods.Add(ParsePodInfoFromToken(obj));
          }
        }
      }
      catch { /* Return partial results */ }

      return pods;
    }

    private static PodInfo ParsePodInfoFromToken(JToken token)
    {
      var info = new PodInfo
      {
        Id = (token["Id"] ?? token["id"])?.Value<string>(),
        Name = (token["Name"] ?? token["name"])?.Value<string>(),
        Status = (token["Status"] ?? token["status"])?.Value<string>(),
        Created = (token["Created"] ?? token["created"])?.Value<string>(),
        InfraId = (token["InfraId"] ?? token["infraId"])?.Value<string>()
      };

      var numContainers = token["NumContainers"] ?? token["num_containers"];
      if (numContainers != null)
        info.NumContainers = numContainers.Value<int>();

      var containers = token["Containers"] ?? token["containers"];
      if (containers is JArray arr)
      {
        foreach (var c in arr)
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
        JToken obj;
        if (trimmed.StartsWith("["))
          obj = JArray.Parse(trimmed).First;
        else
          obj = JObject.Parse(trimmed);

        result.Id = (obj["Id"] ?? obj["id"])?.Value<string>();
        result.Name = (obj["Name"] ?? obj["name"])?.Value<string>();
        result.Created = (obj["Created"] ?? obj["created"])?.Value<string>();
        result.Hostname = (obj["Hostname"] ?? obj["hostname"])?.Value<string>();

        var state = obj["State"] ?? obj["state"];
        if (state != null)
          result.State = state.Type == JTokenType.String
              ? state.Value<string>()
              : state.ToString();

        result.InfraContainerId =
            (obj["InfraContainerId"] ?? obj["infraContainerId"])?.Value<string>();

        var numContainers = obj["NumContainers"] ?? obj["num_containers"];
        if (numContainers != null)
          result.NumContainers = numContainers.Value<int>();

        var containers = obj["Containers"] ?? obj["containers"];
        if (containers is JArray arr)
        {
          foreach (var c in arr)
            result.Containers.Add(ParsePodContainerInfoFromToken(c));
        }
      }
      catch { /* Return partial results */ }

      return result;
    }

    private static PodContainerInfo ParsePodContainerInfoFromToken(JToken token)
    {
      return new PodContainerInfo
      {
        Id = (token["Id"] ?? token["id"])?.Value<string>(),
        Name = (token["Name"] ?? token["name"])?.Value<string>(),
        State = (token["State"] ?? token["state"])?.Value<string>()
      };
    }

    #endregion

    #region Helpers

    private async Task<CommandResponse<Unit>> ExecuteSimplePodCommandAsync(
        string args, string errorCode, CancellationToken cancellationToken)
    {
      try
      {
        var result = await ExecuteCommandAsync(args, cancellationToken);
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
