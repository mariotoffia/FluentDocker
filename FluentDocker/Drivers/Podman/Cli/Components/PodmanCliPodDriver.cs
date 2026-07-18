using System;
using System.Collections.Generic;
using System.Globalization;
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
    /// <summary>Creates a new instance with the specified binary resolver.</summary>
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
        var result = await ExecuteCommandAsync(context, args, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
          return CommandResponse<PodCreateResult>.Fail(
              ErrorOrDefault(result, "Pod create failed"),
              FailureCode(result.Error, ErrorCodes.Pod.CreateFailed),
              CreateErrorContext(context, "CreatePod", result), result.ExitCode);

        return CommandResponse<PodCreateResult>.Ok(new PodCreateResult
        {
          Id = result.Output?.Trim()
        });
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<PodCreateResult>.Fail(
            ex.Message, FailureCode(ex, ErrorCodes.Pod.CreateFailed));
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> StartPodAsync(
        DriverContext context, string name,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = $"pod start {QuotePositionalArgument(name, nameof(name))}";
        var result = await ExecuteUnboundedCommandAsync(context, args, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<Unit>.Fail(
              ErrorOrDefault(result, "Pod start failed"),
              FailureCode(result.Error, ErrorCodes.Pod.StartFailed),
              CreateErrorContext(context, "StartPod", result), result.ExitCode);

        return CommandResponse<Unit>.Ok(Unit.Default);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Pod.StartFailed));
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> StopPodAsync(
        DriverContext context, string name, int? timeout = null,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = timeout.HasValue
            ? $"pod stop -t {timeout.Value.ToString(CultureInfo.InvariantCulture)} {QuotePositionalArgument(name, nameof(name))}"
            : $"pod stop {QuotePositionalArgument(name, nameof(name))}";
        var result = await ExecuteUnboundedCommandAsync(context, args, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<Unit>.Fail(
              ErrorOrDefault(result, "Pod stop failed"),
              FailureCode(result.Error, ErrorCodes.Pod.StopFailed),
              CreateErrorContext(context, "StopPod", result), result.ExitCode);

        return CommandResponse<Unit>.Ok(Unit.Default);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Pod.StopFailed));
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> RestartPodAsync(
        DriverContext context, string name, int? timeout = null,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = timeout.HasValue
            ? $"pod restart -t {timeout.Value.ToString(CultureInfo.InvariantCulture)} {QuotePositionalArgument(name, nameof(name))}"
            : $"pod restart {QuotePositionalArgument(name, nameof(name))}";
        var result = await ExecuteUnboundedCommandAsync(context, args, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<Unit>.Fail(
              ErrorOrDefault(result, "Pod restart failed"),
              FailureCode(result.Error, ErrorCodes.Pod.RestartFailed),
              CreateErrorContext(context, "RestartPod", result), result.ExitCode);

        return CommandResponse<Unit>.Ok(Unit.Default);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Pod.RestartFailed));
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> KillPodAsync(
        DriverContext context, string name, string signal = null,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = !string.IsNullOrEmpty(signal)
            ? $"pod kill --signal {QuotePositionalArgument(signal, nameof(signal))} {QuotePositionalArgument(name, nameof(name))}"
            : $"pod kill {QuotePositionalArgument(name, nameof(name))}";
        var result = await ExecuteCommandAsync(context, args, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<Unit>.Fail(
              ErrorOrDefault(result, "Pod kill failed"),
              FailureCode(result.Error, ErrorCodes.Pod.KillFailed),
              CreateErrorContext(context, "KillPod", result), result.ExitCode);

        return CommandResponse<Unit>.Ok(Unit.Default);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Pod.KillFailed));
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> PausePodAsync(
        DriverContext context, string name,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = $"pod pause {QuotePositionalArgument(name, nameof(name))}";
        var result = await ExecuteCommandAsync(context, args, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<Unit>.Fail(
              ErrorOrDefault(result, "Pod pause failed"),
              FailureCode(result.Error, ErrorCodes.Pod.PauseFailed),
              CreateErrorContext(context, "PausePod", result), result.ExitCode);

        return CommandResponse<Unit>.Ok(Unit.Default);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Pod.PauseFailed));
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> UnpausePodAsync(
        DriverContext context, string name,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = $"pod unpause {QuotePositionalArgument(name, nameof(name))}";
        var result = await ExecuteCommandAsync(context, args, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<Unit>.Fail(
              ErrorOrDefault(result, "Pod unpause failed"),
              FailureCode(result.Error, ErrorCodes.Pod.UnpauseFailed),
              CreateErrorContext(context, "UnpausePod", result), result.ExitCode);

        return CommandResponse<Unit>.Ok(Unit.Default);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Pod.UnpauseFailed));
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> RemovePodAsync(
        DriverContext context, string name, bool force = false,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = force
            ? $"pod rm -f {QuotePositionalArgument(name, nameof(name))}"
            : $"pod rm {QuotePositionalArgument(name, nameof(name))}";
        var result = await ExecuteCommandAsync(context, args, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
        {
          var error = ErrorOrDefault(result, "Pod remove failed");
          return CommandResponse<Unit>.Fail(
              error,
              IsPodNotFound(error) ? ErrorCodes.Pod.NotFound : FailureCode(result.Error, ErrorCodes.Pod.RemoveFailed),
              CreateErrorContext(context, "RemovePod", result), result.ExitCode);
        }

        return CommandResponse<Unit>.Ok(Unit.Default);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Pod.RemoveFailed));
      }
    }

    private static bool IsPodNotFound(string error) =>
        error?.Contains("no such pod", StringComparison.OrdinalIgnoreCase) == true;

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
            context,
            "pod ps --format json", cancellationToken).ConfigureAwait(false);

        if (!result.Success)
          return CommandResponse<IList<PodInfo>>.Fail(
              ErrorOrDefault(result, "Pod list failed"),
              FailureCode(result.Error, ErrorCodes.Pod.ListFailed),
              CreateErrorContext(context, "ListPods", result), result.ExitCode);

        var pods = ParsePodList(result.Output);
        return CommandResponse<IList<PodInfo>>.Ok(pods);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<IList<PodInfo>>.Fail(
            ex.Message, FailureCode(ex, ErrorCodes.Pod.ListFailed));
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
            context,
            $"pod inspect {QuotePositionalArgument(name, nameof(name))}", cancellationToken).ConfigureAwait(false);

        if (!result.Success)
          return CommandResponse<PodInspectResult>.Fail(
              ErrorOrDefault(result, "Pod inspect failed"),
              FailureCode(result.Error, ErrorCodes.Pod.InspectFailed),
              CreateErrorContext(context, "InspectPod", result), result.ExitCode);

        var inspect = ParsePodInspect(result.Output);
        if (string.IsNullOrEmpty(inspect.Id))
          return CommandResponse<PodInspectResult>.Fail(
              $"Pod '{name}' was not found", ErrorCodes.Pod.NotFound,
              CreateErrorContext(context, "InspectPod", result), result.ExitCode);
        return CommandResponse<PodInspectResult>.Ok(inspect);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<PodInspectResult>.Fail(
            ex.Message, FailureCode(ex, ErrorCodes.Pod.InspectFailed));
      }
    }

    #endregion

    #region Argument Building

    private static string BuildCreateArgs(PodCreateConfig config)
    {
      var args = "pod create";

      if (!string.IsNullOrEmpty(config.Name))
        args += $" --name {QuoteArgumentIfNeeded(config.Name)}";
      if (!string.IsNullOrEmpty(config.Hostname))
        args += $" --hostname {QuoteArgumentIfNeeded(config.Hostname)}";
      if (!string.IsNullOrEmpty(config.Network))
        args += $" --network {QuoteArgumentIfNeeded(config.Network)}";
      if (!string.IsNullOrEmpty(config.InfraImage))
        args += $" --infra-image {QuoteArgumentIfNeeded(config.InfraImage)}";
      if (!string.IsNullOrEmpty(config.Share))
        args += $" --share {QuoteArgumentIfNeeded(config.Share)}";

      foreach (var label in OrEmpty(config.Labels))
        args += $" --label {QuoteArgumentIfNeeded($"{label.Key}={label.Value}")}";
      foreach (var dns in OrEmpty(config.Dns))
        args += $" --dns {QuoteArgumentIfNeeded(dns)}";
      foreach (var port in OrEmpty(config.Ports))
        args += $" -p {QuoteArgumentIfNeeded(port)}";

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
      catch (Exception ex)
      {
        throw new FluentDockerException(
            $"Failed to parse Podman pod list output: {ex.Message}");
      }

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
      if (info.NumContainers == 0)
        info.NumContainers = info.Containers.Count;

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
          using var enumerator = root.EnumerateArray();
          if (!enumerator.MoveNext())
            return result;
          obj = enumerator.Current;
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
            obj.GetStringOrDefault("InfraContainerID")
            ?? obj.GetStringOrDefault("InfraContainerId", "infraContainerId");

        var numProp = obj.Prop("NumContainers", "num_containers");
        if (numProp.HasValue && numProp.Value.ValueKind == JsonValueKind.Number)
          result.NumContainers = numProp.Value.GetInt32();

        var containers = obj.Prop("Containers", "containers");
        if (containers.HasValue && containers.Value.ValueKind == JsonValueKind.Array)
        {
          foreach (var c in containers.Value.EnumerateArray())
            result.Containers.Add(ParsePodContainerInfoFromToken(c));
        }
        if (result.NumContainers == 0)
          result.NumContainers = result.Containers.Count;
      }
      catch (Exception ex)
      {
        throw new FluentDockerException(
            $"Failed to parse Podman pod inspect output: {ex.Message}");
      }

      return result;
    }

    private static PodContainerInfo ParsePodContainerInfoFromToken(JsonElement token)
    {
      return new PodContainerInfo
      {
        Id = token.GetStringOrDefault("Id", "id"),
        Name = token.GetStringOrDefault("Names")
               ?? token.GetStringOrDefault("Name", "name"),
        State = token.GetStringOrDefault("Status")
                ?? token.GetStringOrDefault("State", "state")
      };
    }

    #endregion

  }
}
