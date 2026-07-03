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
          args.Add($"--name {QuotePositionalArgument(config.Name, nameof(config.Name))}");
        if (config.Replicas.HasValue)
          args.Add($"--replicas {config.Replicas.Value}");
        if (!string.IsNullOrEmpty(config.Mode))
          args.Add($"--mode {QuoteArgumentIfNeeded(config.Mode)}");
        foreach (var env in config.Environment)
          args.Add($"-e {QuoteArgumentIfNeeded($"{env.Key}={env.Value}")}");
        foreach (var label in config.Labels)
          args.Add($"--label {QuoteArgumentIfNeeded($"{label.Key}={label.Value}")}");
        foreach (var port in config.Ports)
          args.Add($"-p {QuoteArgumentIfNeeded($"{port.PublishedPort}:{port.TargetPort}/{port.Protocol}")}");
        foreach (var network in config.Networks)
          args.Add($"--network {QuoteArgumentIfNeeded(network)}");
        if (config.Detach)
          args.Add("-d");
        if (config.Quiet)
          args.Add("-q");

        args.Add(QuotePositionalArgument(config.Image, nameof(config.Image)));
        if (config.Command != null)
          foreach (var cmd in config.Command)
            args.Add(QuoteArgumentIfNeeded(cmd));

        var command = string.Join(" ", args);
        var result = config.Detach
            ? await ExecuteCommandAsync(context, command, cancellationToken).ConfigureAwait(false)
            : await ExecuteUnboundedCommandAsync(context, command, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<ServiceCreateResult>.Fail(
              ErrorOrDefault(result, "Service create failed"), FailureCode(result.Error, ErrorCodes.Service.CreateFailed));
        }

        return CommandResponse<ServiceCreateResult>.Ok(new ServiceCreateResult { Id = result.Output.Trim() });
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<ServiceCreateResult>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Service.CreateFailed));
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
        var result = await ExecuteCommandAsync(context, $"service rm {string.Join(" ", serviceIds.Select(id => QuotePositionalArgument(id, nameof(serviceIds))))}", cancellationToken).ConfigureAwait(false);
        return result.Success
            ? CommandResponse<Unit>.Ok(Unit.Default)
            : CommandResponse<Unit>.Fail(ErrorOrDefault(result, "Service rm failed"), FailureCode(result.Error, ErrorCodes.Service.RemoveFailed));
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Service.RemoveFailed));
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
          args.Add($"--image {QuotePositionalArgument(config.Image, nameof(config.Image))}");
        if (config.Replicas.HasValue)
          args.Add($"--replicas {config.Replicas.Value}");
        foreach (var env in config.EnvAdd)
          args.Add($"--env-add {QuoteArgumentIfNeeded($"{env.Key}={env.Value}")}");
        foreach (var env in config.EnvRm)
          args.Add($"--env-rm {QuoteArgumentIfNeeded(env)}");
        if (config.Force)
          args.Add("--force");
        if (config.Detach)
          args.Add("-d");

        args.Add(QuotePositionalArgument(serviceId, nameof(serviceId)));

        var result = await ExecuteCommandAsync(context, string.Join(" ", args), cancellationToken).ConfigureAwait(false);
        return result.Success
            ? CommandResponse<Unit>.Ok(Unit.Default)
            : CommandResponse<Unit>.Fail(ErrorOrDefault(result, "Service update failed"), FailureCode(result.Error, ErrorCodes.Service.UpdateFailed));
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Service.UpdateFailed));
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
        args += $" {QuotePositionalArgument(serviceId, nameof(serviceId))}";

        var result = await ExecuteCommandAsync(context, args, cancellationToken).ConfigureAwait(false);
        return result.Success
            ? CommandResponse<Unit>.Ok(Unit.Default)
            : CommandResponse<Unit>.Fail(ErrorOrDefault(result, "Service rollback failed"), FailureCode(result.Error, ErrorCodes.Service.RollbackFailed));
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Service.RollbackFailed));
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
            args += $" --filter {QuoteArgumentIfNeeded($"name={filter.Name}")}";
          if (!string.IsNullOrEmpty(filter.Id))
            args += $" --filter {QuoteArgumentIfNeeded($"id={filter.Id}")}";
          foreach (var label in filter.Labels)
            args += $" --filter {QuoteArgumentIfNeeded($"label={label.Key}={label.Value}")}";
        }

        var result = await ExecuteCommandAsync(context, args, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<IList<ServiceInfo>>.Fail(
              ErrorOrDefault(result, "Service list failed"), FailureCode(result.Error, ErrorCodes.Service.ListFailed));
        }

        if (!DockerCliJsonLineParser.TryParse(
                result.Output,
                Logger,
                "Service list JSON parsing failed",
                out List<ServiceInfo> services,
                out var parseError))
        {
          return CommandResponse<IList<ServiceInfo>>.Fail(parseError, ErrorCodes.Service.ListFailed);
        }

        return CommandResponse<IList<ServiceInfo>>.Ok(services);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<IList<ServiceInfo>>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Service.ListFailed));
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
        args += $" {QuotePositionalArgument(serviceId, nameof(serviceId))}";

        var result = await ExecuteCommandAsync(context, args, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<ServiceDetails>.Fail(
              ErrorOrDefault(result, "Service inspect failed"), FailureCode(result.Error, ErrorCodes.Service.InspectFailed));
        }

        var details = ParseServiceInspect(result.Output);
        return details != null
            ? CommandResponse<ServiceDetails>.Ok(details)
            : CommandResponse<ServiceDetails>.Fail("Service not found", ErrorCodes.Service.NotFound);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<ServiceDetails>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Service.InspectFailed));
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
        var args = $"service ps --format \"{{{{json .}}}}\" {QuotePositionalArgument(serviceId, nameof(serviceId))}";

        var result = await ExecuteCommandAsync(context, args, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<IList<ServiceTask>>.Fail(
              ErrorOrDefault(result, "Service ps failed"), FailureCode(result.Error, ErrorCodes.Service.TasksFailed));
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
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<IList<ServiceTask>>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Service.TasksFailed));
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
        if (config?.Follow == true)
        {
          throw new NotSupportedException(
              "GetLogsAsync does not support follow=true because 'docker service logs -f' " +
              "streams indefinitely. Use a streaming logs API instead.");
        }

        var args = "service logs";
        if (config?.Timestamps == true)
          args += " -t";
        if (config?.Tail.HasValue == true)
          args += $" --tail {config.Tail.Value}";
        args += $" {QuotePositionalArgument(serviceId, nameof(serviceId))}";

        var result = await ExecuteCommandAsync(context, args, cancellationToken).ConfigureAwait(false);
        return result.Success
            ? CommandResponse<string>.Ok(result.Output)
            : CommandResponse<string>.Fail(ErrorOrDefault(result, "Service logs failed"), FailureCode(result.Error, ErrorCodes.Service.LogsFailed));
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<string>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Service.LogsFailed));
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
        var scaleArgs = string.Join(" ", serviceReplicas.Select(sr => QuoteArgumentIfNeeded($"{sr.Key}={sr.Value}")));
        var args = $"service scale {scaleArgs}";
        if (detach)
          args = args.Replace("service scale", "service scale -d");

        var result = await ExecuteCommandAsync(context, args, cancellationToken).ConfigureAwait(false);
        return result.Success
            ? CommandResponse<Unit>.Ok(Unit.Default)
            : CommandResponse<Unit>.Fail(ErrorOrDefault(result, "Service scale failed"), FailureCode(result.Error, ErrorCodes.Service.ScaleFailed));
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Service.ScaleFailed));
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
        _ = DateTime.TryParse(obj.GetStringOrDefault("CreatedAt"), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _);
      if (obj.Prop("UpdatedAt").HasValue)
        _ = DateTime.TryParse(obj.GetStringOrDefault("UpdatedAt"), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _);

      return details;
    }
  }
}
