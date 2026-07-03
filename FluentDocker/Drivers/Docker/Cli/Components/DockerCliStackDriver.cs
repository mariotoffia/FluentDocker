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

        var result = await ExecuteCommandAsync(context, args, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<IList<StackInfo>>.Fail(
              ErrorOrDefault(result, "Stack list failed"), FailureCode(result.Error, ErrorCodes.Stack.ListFailed));
        }

        if (!DockerCliJsonLineParser.TryParse(
                result.Output,
                Logger,
                "Stack list JSON parsing failed",
                out List<StackInfo> stacks,
                out var parseError))
        {
          return CommandResponse<IList<StackInfo>>.Fail(parseError, ErrorCodes.Stack.ListFailed);
        }

        return CommandResponse<IList<StackInfo>>.Ok(stacks);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<IList<StackInfo>>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Stack.ListFailed));
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
        var args = $"stack ps --format \"{{{{json .}}}}\" {QuotePositionalArgument(stackName, nameof(stackName))}";
        if (filter?.NoTrunc == true)
          args = args.Replace("stack ps", "stack ps --no-trunc");

        var result = await ExecuteCommandAsync(context, args, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<IList<StackTask>>.Fail(
              ErrorOrDefault(result, "Stack ps failed"), FailureCode(result.Error, ErrorCodes.Stack.TasksFailed));
        }

        if (!DockerCliJsonLineParser.TryParse(
                result.Output,
                Logger,
                "Stack task JSON parsing failed",
                out List<StackTask> tasks,
                out var parseError))
        {
          return CommandResponse<IList<StackTask>>.Fail(parseError, ErrorCodes.Stack.TasksFailed);
        }

        return CommandResponse<IList<StackTask>>.Ok(tasks);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<IList<StackTask>>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Stack.TasksFailed));
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
          args += $" -c {QuoteArgumentIfNeeded(file)}";
        if (config.Prune)
          args += " --prune";
        if (config.WithRegistryAuth)
          args += " --with-registry-auth";
        args += $" {QuoteArgumentIfNeeded(config.StackName)}";

        var result = await ExecuteCommandAsync(context, args, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<StackDeployResult>.Fail(
              ErrorOrDefault(result, "Stack deploy failed"), FailureCode(result.Error, ErrorCodes.Stack.DeployFailed));
        }

        return CommandResponse<StackDeployResult>.Ok(new StackDeployResult { StackName = config.StackName });
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<StackDeployResult>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Stack.DeployFailed));
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
        var args = $"stack rm {string.Join(" ", stackNames.Select(QuoteArgumentIfNeeded))}";

        var result = await ExecuteCommandAsync(context, args, cancellationToken).ConfigureAwait(false);

        return result.Success
            ? CommandResponse<Unit>.Ok(Unit.Default)
            : CommandResponse<Unit>.Fail(ErrorOrDefault(result, "Stack rm failed"), FailureCode(result.Error, ErrorCodes.Stack.RemoveFailed));
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Stack.RemoveFailed));
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
        var args = $"stack services --format \"{{{{json .}}}}\" {QuotePositionalArgument(stackName, nameof(stackName))}";

        var result = await ExecuteCommandAsync(context, args, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<IList<StackServiceInfo>>.Fail(
              ErrorOrDefault(result, "Stack services failed"), FailureCode(result.Error, ErrorCodes.Stack.ServicesFailed));
        }

        if (!DockerCliJsonLineParser.TryParse(
                result.Output,
                Logger,
                "Stack service JSON parsing failed",
                out List<StackServiceInfo> services,
                out var parseError))
        {
          return CommandResponse<IList<StackServiceInfo>>.Fail(parseError, ErrorCodes.Stack.ServicesFailed);
        }

        return CommandResponse<IList<StackServiceInfo>>.Ok(services);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<IList<StackServiceInfo>>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Stack.ServicesFailed));
      }
    }
  }
}
