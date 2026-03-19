using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using System.Text.Json;
using FluentDocker.Drivers.Docker.Cli.Binary;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Drivers.Docker.Cli.Components
{
  /// <summary>
  /// Docker CLI implementation of IStackDriver.
  /// </summary>
  public class DockerCliStackDriver : DockerCliDriverBase, IStackDriver
  {
    private static readonly char[] LineSeparators = ['\n', '\r'];
    /// <summary>
    /// Creates a new instance with the specified binary resolver.
    /// </summary>
    public DockerCliStackDriver(IBinaryResolver binaryResolver) : base(binaryResolver)
    {
    }

    /// <inheritdoc />
    public async Task<CommandResponse<IList<StackInfo>>> ListAsync(
        DriverContext context,
        StackListFilter filter = null,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = "stack ls --format \"{{json .}}\"";

        var result = await ExecuteCommandAsync(args, cancellationToken);

        if (!result.Success)
        {
          return CommandResponse<IList<StackInfo>>.Fail(
              result.Error ?? "Stack list failed", ErrorCodes.Stack.ListFailed);
        }

        var stacks = new List<StackInfo>();
        var lines = result.Output.Split(LineSeparators, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
          try
          {
            var stack = JsonSerializer.Deserialize<StackInfo>(line, JsonHelper.CaseInsensitiveOptions);
            if (stack != null)
              stacks.Add(stack);
          }
          catch (Exception ex)
          {
            Logger.Log($"Stack list JSON parsing failed: {ex.Message}");
          }
        }

        return CommandResponse<IList<StackInfo>>.Ok(stacks);
      }
      catch (Exception ex)
      {
        return CommandResponse<IList<StackInfo>>.Fail(ex.Message, ErrorCodes.Stack.ListFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<IList<StackTask>>> GetTasksAsync(
        DriverContext context,
        string stackName,
        StackTaskFilter filter = null,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = $"stack ps --format \"{{{{json .}}}}\" {stackName}";
        if (filter?.NoTrunc == true)
          args = args.Replace("stack ps", "stack ps --no-trunc");

        var result = await ExecuteCommandAsync(args, cancellationToken);

        if (!result.Success)
        {
          return CommandResponse<IList<StackTask>>.Fail(
              result.Error ?? "Stack ps failed", ErrorCodes.Stack.TasksFailed);
        }

        var tasks = new List<StackTask>();
        var lines = result.Output.Split(LineSeparators, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
          try
          {
            var task = JsonSerializer.Deserialize<StackTask>(line, JsonHelper.CaseInsensitiveOptions);
            if (task != null)
              tasks.Add(task);
          }
          catch (Exception ex)
          {
            Logger.Log($"Stack task JSON parsing failed: {ex.Message}");
          }
        }

        return CommandResponse<IList<StackTask>>.Ok(tasks);
      }
      catch (Exception ex)
      {
        return CommandResponse<IList<StackTask>>.Fail(ex.Message, ErrorCodes.Stack.TasksFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<StackDeployResult>> DeployAsync(
        DriverContext context,
        StackDeployConfig config,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = "stack deploy";
        foreach (var file in config.ComposeFiles)
          args += $" -c {file}";
        if (config.Prune)
          args += " --prune";
        if (config.WithRegistryAuth)
          args += " --with-registry-auth";
        args += $" {config.StackName}";

        var result = await ExecuteCommandAsync(args, cancellationToken);

        if (!result.Success)
        {
          return CommandResponse<StackDeployResult>.Fail(
              result.Error ?? "Stack deploy failed", ErrorCodes.Stack.DeployFailed);
        }

        return CommandResponse<StackDeployResult>.Ok(new StackDeployResult { StackName = config.StackName });
      }
      catch (Exception ex)
      {
        return CommandResponse<StackDeployResult>.Fail(ex.Message, ErrorCodes.Stack.DeployFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> RemoveAsync(
        DriverContext context,
        string[] stackNames,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = $"stack rm {string.Join(" ", stackNames)}";

        var result = await ExecuteCommandAsync(args, cancellationToken);

        return result.Success
            ? CommandResponse<Unit>.Ok(Unit.Default)
            : CommandResponse<Unit>.Fail(result.Error ?? "Stack rm failed", ErrorCodes.Stack.RemoveFailed);
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Stack.RemoveFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<IList<StackServiceInfo>>> GetServicesAsync(
        DriverContext context,
        string stackName,
        StackServiceFilter filter = null,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = $"stack services --format \"{{{{json .}}}}\" {stackName}";

        var result = await ExecuteCommandAsync(args, cancellationToken);

        if (!result.Success)
        {
          return CommandResponse<IList<StackServiceInfo>>.Fail(
              result.Error ?? "Stack services failed", ErrorCodes.Stack.ServicesFailed);
        }

        var services = new List<StackServiceInfo>();
        var lines = result.Output.Split(LineSeparators, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
          try
          {
            var svc = JsonSerializer.Deserialize<StackServiceInfo>(line, JsonHelper.CaseInsensitiveOptions);
            if (svc != null)
              services.Add(svc);
          }
          catch (Exception ex)
          {
            Logger.Log($"Stack service JSON parsing failed: {ex.Message}");
          }
        }

        return CommandResponse<IList<StackServiceInfo>>.Ok(services);
      }
      catch (Exception ex)
      {
        return CommandResponse<IList<StackServiceInfo>>.Fail(ex.Message, ErrorCodes.Stack.ServicesFailed);
      }
    }
  }
}

