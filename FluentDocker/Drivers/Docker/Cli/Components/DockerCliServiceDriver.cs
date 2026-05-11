using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers.Docker.Cli.Binary;
using FluentDocker.Model.Drivers;
using Microsoft.Extensions.Logging;

namespace FluentDocker.Drivers.Docker.Cli.Components
{
  /// <summary>
  /// Docker CLI implementation of IServiceDriver (for Docker Swarm services).
  /// </summary>
  public class DockerCliServiceDriver : DockerCliDriverBase, IServiceDriver
  {
    private static readonly char[] LineSeparators = ['\n', '\r'];
    /// <summary>
    /// Creates a new instance with the specified binary resolver.
    /// </summary>
    public DockerCliServiceDriver(IBinaryResolver binaryResolver) : base(binaryResolver)
    {
    }

    /// <inheritdoc />
    public async Task<CommandResponse<ServiceCreateResult>> CreateAsync(
        DriverContext context,
        ServiceCreateConfig config,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = new List<string> { "service", "create" };

        if (!string.IsNullOrEmpty(config.Name))
          args.Add($"--name {config.Name}");
        if (config.Replicas.HasValue)
          args.Add($"--replicas {config.Replicas.Value}");
        if (!string.IsNullOrEmpty(config.Mode))
          args.Add($"--mode {config.Mode}");
        foreach (var env in config.Environment)
          args.Add($"-e {env.Key}={env.Value}");
        foreach (var label in config.Labels)
          args.Add($"--label {label.Key}={label.Value}");
        foreach (var port in config.Ports)
          args.Add($"-p {port.PublishedPort}:{port.TargetPort}/{port.Protocol}");
        foreach (var network in config.Networks)
          args.Add($"--network {network}");
        if (config.Detach)
          args.Add("-d");
        if (config.Quiet)
          args.Add("-q");

        args.Add(config.Image);
        if (config.Command != null)
          args.AddRange(config.Command);

        var result = await ExecuteCommandAsync(string.Join(" ", args), cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<ServiceCreateResult>.Fail(
              result.Error ?? "Service create failed", ErrorCodes.Service.CreateFailed);
        }

        return CommandResponse<ServiceCreateResult>.Ok(new ServiceCreateResult { Id = result.Output.Trim() });
      }
      catch (Exception ex)
      {
        return CommandResponse<ServiceCreateResult>.Fail(ex.Message, ErrorCodes.Service.CreateFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> RemoveAsync(
        DriverContext context,
        string[] serviceIds,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteCommandAsync($"service rm {string.Join(" ", serviceIds)}", cancellationToken).ConfigureAwait(false);
        return result.Success
            ? CommandResponse<Unit>.Ok(Unit.Default)
            : CommandResponse<Unit>.Fail(result.Error ?? "Service rm failed", ErrorCodes.Service.RemoveFailed);
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Service.RemoveFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> UpdateAsync(
        DriverContext context,
        string serviceId,
        ServiceUpdateConfig config,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = new List<string> { "service", "update" };

        if (!string.IsNullOrEmpty(config.Image))
          args.Add($"--image {config.Image}");
        if (config.Replicas.HasValue)
          args.Add($"--replicas {config.Replicas.Value}");
        foreach (var env in config.EnvAdd)
          args.Add($"--env-add {env.Key}={env.Value}");
        foreach (var env in config.EnvRm)
          args.Add($"--env-rm {env}");
        if (config.Force)
          args.Add("--force");
        if (config.Detach)
          args.Add("-d");

        args.Add(serviceId);

        var result = await ExecuteCommandAsync(string.Join(" ", args), cancellationToken).ConfigureAwait(false);
        return result.Success
            ? CommandResponse<Unit>.Ok(Unit.Default)
            : CommandResponse<Unit>.Fail(result.Error ?? "Service update failed", ErrorCodes.Service.UpdateFailed);
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Service.UpdateFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> RollbackAsync(
        DriverContext context,
        string serviceId,
        bool detach = false,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = "service rollback";
        if (detach)
          args += " -d";
        args += $" {serviceId}";

        var result = await ExecuteCommandAsync(args, cancellationToken).ConfigureAwait(false);
        return result.Success
            ? CommandResponse<Unit>.Ok(Unit.Default)
            : CommandResponse<Unit>.Fail(result.Error ?? "Service rollback failed", ErrorCodes.Service.RollbackFailed);
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Service.RollbackFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<IList<ServiceInfo>>> ListAsync(
        DriverContext context,
        ServiceListFilter filter = null,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = "service ls --format \"{{json .}}\"";
        if (filter?.Quiet == true)
          args = "service ls -q";
        else if (filter != null)
        {
          if (!string.IsNullOrEmpty(filter.Name))
            args += $" --filter name={filter.Name}";
          if (!string.IsNullOrEmpty(filter.Id))
            args += $" --filter id={filter.Id}";
          foreach (var label in filter.Labels)
            args += $" --filter label={label.Key}={label.Value}";
        }

        var result = await ExecuteCommandAsync(args, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<IList<ServiceInfo>>.Fail(
              result.Error ?? "Service list failed", ErrorCodes.Service.ListFailed);
        }

        var services = new List<ServiceInfo>();
        var lines = result.Output.Split(LineSeparators, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
          try
          {
            var svc = JsonSerializer.Deserialize<ServiceInfo>(line, JsonHelper.CaseInsensitiveOptions);
            if (svc != null)
              services.Add(svc);
          }
          catch (Exception ex)
          {
            Logger.LogError(ex, "Service list JSON parsing failed");
          }
        }

        return CommandResponse<IList<ServiceInfo>>.Ok(services);
      }
      catch (Exception ex)
      {
        return CommandResponse<IList<ServiceInfo>>.Fail(ex.Message, ErrorCodes.Service.ListFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<ServiceDetails>> InspectAsync(
        DriverContext context,
        string serviceId,
        bool pretty = false,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = "service inspect";
        if (pretty)
          args += " --pretty";
        args += $" {serviceId}";

        var result = await ExecuteCommandAsync(args, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<ServiceDetails>.Fail(
              result.Error ?? "Service inspect failed", ErrorCodes.Service.InspectFailed);
        }

        var details = ParseServiceInspect(result.Output);
        return details != null
            ? CommandResponse<ServiceDetails>.Ok(details)
            : CommandResponse<ServiceDetails>.Fail("Service not found", ErrorCodes.Service.NotFound);
      }
      catch (Exception ex)
      {
        return CommandResponse<ServiceDetails>.Fail(ex.Message, ErrorCodes.Service.InspectFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<IList<ServiceTask>>> GetTasksAsync(
        DriverContext context,
        string serviceId,
        ServiceTaskFilter filter = null,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = $"service ps --format \"{{{{json .}}}}\" {serviceId}";

        var result = await ExecuteCommandAsync(args, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<IList<ServiceTask>>.Fail(
              result.Error ?? "Service ps failed", ErrorCodes.Service.TasksFailed);
        }

        var tasks = new List<ServiceTask>();
        var lines = result.Output.Split(LineSeparators, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
          try
          {
            var task = JsonSerializer.Deserialize<ServiceTask>(line, JsonHelper.CaseInsensitiveOptions);
            if (task != null)
              tasks.Add(task);
          }
          catch (Exception ex)
          {
            Logger.LogError(ex, "Service task JSON parsing failed");
          }
        }

        return CommandResponse<IList<ServiceTask>>.Ok(tasks);
      }
      catch (Exception ex)
      {
        return CommandResponse<IList<ServiceTask>>.Fail(ex.Message, ErrorCodes.Service.TasksFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<string>> GetLogsAsync(
        DriverContext context,
        string serviceId,
        ServiceLogsConfig config = null,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = "service logs";
        if (config?.Follow == true)
          args += " -f";
        if (config?.Timestamps == true)
          args += " -t";
        if (config?.Tail.HasValue == true)
          args += $" --tail {config.Tail.Value}";
        args += $" {serviceId}";

        var result = await ExecuteCommandAsync(args, cancellationToken).ConfigureAwait(false);
        return result.Success
            ? CommandResponse<string>.Ok(result.Output)
            : CommandResponse<string>.Fail(result.Error ?? "Service logs failed", ErrorCodes.Service.LogsFailed);
      }
      catch (Exception ex)
      {
        return CommandResponse<string>.Fail(ex.Message, ErrorCodes.Service.LogsFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> ScaleAsync(
        DriverContext context,
        Dictionary<string, int> serviceReplicas,
        bool detach = false,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var scaleArgs = string.Join(" ", serviceReplicas.Select(sr => $"{sr.Key}={sr.Value}"));
        var args = $"service scale {scaleArgs}";
        if (detach)
          args = args.Replace("service scale", "service scale -d");

        var result = await ExecuteCommandAsync(args, cancellationToken).ConfigureAwait(false);
        return result.Success
            ? CommandResponse<Unit>.Ok(Unit.Default)
            : CommandResponse<Unit>.Fail(result.Error ?? "Service scale failed", ErrorCodes.Service.ScaleFailed);
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Service.ScaleFailed);
      }
    }

    /// <summary>
    /// Parses the JSON output from docker service inspect into a ServiceDetails.
    /// Handles the nested Version object (Version.Index).
    /// </summary>
    internal static ServiceDetails ParseServiceInspect(string json)
    {
      var root = JsonHelper.ParseElement(json);
      if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
        return null;

      var obj = root[0];
      if (obj.ValueKind != JsonValueKind.Object)
        return null;

      var spec = obj.Prop("Spec");
      var taskTemplate = spec?.Prop("TaskTemplate");
      var containerSpec = taskTemplate?.Prop("ContainerSpec");

      var details = new ServiceDetails
      {
        Id = obj.GetStringOrDefault("ID"),
        Version = spec != null ? obj.Prop("Version")?.GetInt64OrDefault("Index") ?? 0 : 0,
        Name = spec?.GetStringOrDefault("Name"),
        Image = containerSpec?.GetStringOrDefault("Image"),
        RawJson = json
      };

      // Fix: Version is from obj, not spec
      var versionEl = obj.Prop("Version");
      if (versionEl.HasValue)
        details.Version = versionEl.Value.GetInt64OrDefault("Index");

      // Mode and replicas
      var mode = spec?.Prop("Mode");
      if (mode.HasValue)
      {
        var replicated = mode.Value.Prop("Replicated");
        if (replicated.HasValue)
        {
          details.Mode = "replicated";
          details.Replicas = (int)(replicated.Value.GetInt64OrDefault("Replicas"));
        }
        else if (mode.Value.Prop("Global").HasValue)
        {
          details.Mode = "global";
        }
      }

      // Command and args
      if (containerSpec.HasValue)
      {
        details.Command = containerSpec.Value.GetStringArray("Command");
        details.Args = containerSpec.Value.GetStringArray("Args");

        // Environment
        var envArr = containerSpec.Value.GetStringArray("Env");
        foreach (var e in envArr)
        {
          var parts = e.Split('=', 2);
          if (parts.Length == 2)
            details.Environment[parts[0]] = parts[1];
        }
      }

      // Labels
      if (spec.HasValue)
      {
        var labelsDict = spec.Value.GetStringDictionary("Labels");
        foreach (var kv in labelsDict)
          details.Labels[kv.Key] = kv.Value;
      }

      // Timestamps — ServiceDetails does not yet have date fields; parse is a no-op placeholder
      if (obj.Prop("CreatedAt").HasValue)
        _ = DateTime.TryParse(obj.GetStringOrDefault("CreatedAt"), out _);
      if (obj.Prop("UpdatedAt").HasValue)
        _ = DateTime.TryParse(obj.GetStringOrDefault("UpdatedAt"), out _);

      return details;
    }
  }
}

