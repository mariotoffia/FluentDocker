using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Model.Drivers;
using Microsoft.Extensions.Logging;
using Container = FluentDocker.Model.Containers.Container;
using ContainerState = FluentDocker.Model.Containers.ContainerState;

namespace FluentDocker.Drivers.Docker.Cli.Components
{
  /// <summary>
  /// Docker CLI container driver - inspection, listing, logs, stats, and query operations.
  /// </summary>
  public partial class DockerCliContainerDriver
  {
    private static readonly char[] LineSeparators = ['\n', '\r'];
    private static readonly char[] SpaceSeparator = [' '];
    private static readonly string[] SlashSeparator = [" / "];
    #region Information Operations

    /// <inheritdoc />
    public async Task<CommandResponse<Container>> InspectAsync(
        DriverContext context,
        string containerId,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteCommandAsync(context, $"container inspect {QuotePositionalArgument(containerId, nameof(containerId))}", cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<Container>.Fail(
              ErrorOrDefault(result, "Container inspect failed"),
              result.Error?.Contains("No such container", StringComparison.OrdinalIgnoreCase) == true
                  ? ErrorCodes.Container.NotFound
                  : FailureCode(result.Error, ErrorCodes.Container.InspectFailed),
              CreateErrorContext(context, "InspectContainer", result),
              result.ExitCode);
        }

        if (!JsonHelper.TryDeserialize<List<Container>>(result.Output, out var containers, out var parseError))
        {
          Logger.LogError(parseError, "Container inspect JSON parsing failed");
          var error = parseError == null
              ? "Container inspect JSON parsing failed"
              : $"Container inspect JSON parsing failed: {parseError.Message}";
          return CommandResponse<Container>.Fail(
              error,
              ErrorCodes.Container.InspectFailed);
        }

        var container = containers?.FirstOrDefault();

        if (container == null)
        {
          return CommandResponse<Container>.Fail(
              $"Container {containerId} not found",
              ErrorCodes.Container.NotFound);
        }

        container.Name = container.Name?.TrimStart('/');
        return CommandResponse<Container>.Ok(container);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<Container>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Container.InspectFailed));
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<IList<Container>>> ListAsync(
        DriverContext context,
        ContainerListFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = "ps --format \"{{json .}}\"";

        if (filter?.All == true)
          args += " -a";

        // Add label filters
        if (filter?.Labels != null && filter.Labels.Count > 0)
        {
          foreach (var label in filter.Labels)
          {
            if (string.IsNullOrEmpty(label.Value))
              args += $" --filter {QuoteArgumentIfNeeded($"label={label.Key}")}";
            else
              args += $" --filter {QuoteArgumentIfNeeded($"label={label.Key}={label.Value}")}";
          }
        }

        // Add name filter
        if (!string.IsNullOrEmpty(filter?.Name))
          args += $" --filter {QuoteArgumentIfNeeded($"name={filter.Name}")}";

        // Add status filter
        if (!string.IsNullOrEmpty(filter?.Status))
          args += $" --filter {QuoteArgumentIfNeeded($"status={filter.Status}")}";

        // Add ID filter
        if (!string.IsNullOrEmpty(filter?.Id))
          args += $" --filter {QuoteArgumentIfNeeded($"id={filter.Id}")}";

        // Add ancestor filter
        if (!string.IsNullOrEmpty(filter?.Ancestor))
          args += $" --filter {QuoteArgumentIfNeeded($"ancestor={filter.Ancestor}")}";

        // ponytail: --last implies all-states and treats non-positive as "no limit"/"none"; ignore <= 0.
        if (filter?.Limit is int limit && limit > 0)
          args += $" --last {limit}";

        var result = await ExecuteCommandAsync(context, args, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<IList<Container>>.Fail(
              ErrorOrDefault(result, "Container list failed"),
              FailureCode(result.Error, ErrorCodes.General.Unknown));
        }

        if (!DockerCliJsonLineParser.TryParse<DockerPsDto>(
            result.Output,
            Logger,
            "Container list JSON parsing failed",
            out var dtos,
            out var parseError))
          return CommandResponse<IList<Container>>.Fail(parseError, ErrorCodes.General.Unknown);

        var containers = new List<Container>();
        foreach (var dto in dtos)
        {
          var container = new Container
          {
            Id = dto.ID,
            Image = dto.Image,
            Name = dto.Names
          };

          if (DockerCliTimestampParser.TryParse(dto.CreatedAt, out DateTimeOffset created))
          {
            container.Created = created;
          }
          else if (!string.IsNullOrEmpty(dto.CreatedAt) && Logger.IsEnabled(LogLevel.Debug))
          {
            Logger.LogDebug("Unparseable container CreatedAt '{CreatedAt}'", dto.CreatedAt);
          }

          if (!string.IsNullOrEmpty(dto.State))
            container.State = new ContainerState
            {
              Running = dto.State.Equals("running", StringComparison.OrdinalIgnoreCase),
              Status = dto.Status
            };

          containers.Add(container);
        }

        return CommandResponse<IList<Container>>.Ok(containers);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<IList<Container>>.Fail(ex.Message, FailureCode(ex, ErrorCodes.General.Unknown));
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<string>> GetLogsAsync(
        DriverContext context,
        string containerId,
        bool follow = false,
        int? tail = null,
        bool timestamps = false,
        CancellationToken cancellationToken = default)
    {
      try
      {
        if (follow)
        {
          return CommandResponse<string>.Fail(
              "GetLogsAsync does not support follow=true because 'docker logs -f' " +
              "streams indefinitely. Use IStreamDriver.StreamLogsAsync instead.",
              ErrorCodes.Container.LogsFailed);
        }

        var args = "logs";
        if (tail.HasValue)
          args += $" --tail {FormatInvariant(tail.Value)}";
        if (timestamps)
          args += " -t";
        args += $" {QuotePositionalArgument(containerId, nameof(containerId))}";

        var result = await ExecuteUnboundedCommandAsync(context, args, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<string>.Fail(
              ErrorOrDefault(result, "Get logs failed"),
              FailureCode(result.Error, ErrorCodes.Container.LogsFailed));
        }

        return CommandResponse<string>.Ok(MergeOutputAndError(result.Output, result.Error));
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<string>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Container.LogsFailed));
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<ContainerProcesses>> TopAsync(
        DriverContext context,
        string containerId,
        string? psOptions = null,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = $"top {QuotePositionalArgument(containerId, nameof(containerId))}";
        if (!string.IsNullOrWhiteSpace(psOptions))
          args += " " + string.Join(" ", psOptions.Split(SpaceSeparator, StringSplitOptions.RemoveEmptyEntries).Select(QuoteArgumentIfNeeded));

        var result = await ExecuteCommandAsync(context, args, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<ContainerProcesses>.Fail(
              ErrorOrDefault(result, "Container top failed"),
              FailureCode(result.Error, ErrorCodes.Container.TopFailed),
              CreateErrorContext(context, "TopContainer", result),
              result.ExitCode);
        }

        var processes = new ContainerProcesses();
        var lines = result.Output.Split(LineSeparators, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length > 0)
        {
          var titles = lines[0].Split(SpaceSeparator, StringSplitOptions.RemoveEmptyEntries);
          processes.Titles = [.. titles];
          for (var i = 1; i < lines.Length; i++)
          {
            // ponytail: count-limited split keeps the last column (CMD) intact with its spaces
            processes.Processes.Add([.. lines[i].Split(SpaceSeparator, titles.Length, StringSplitOptions.RemoveEmptyEntries)]);
          }
        }

        return CommandResponse<ContainerProcesses>.Ok(processes);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<ContainerProcesses>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Container.TopFailed));
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<IList<FilesystemChange>>> DiffAsync(
        DriverContext context,
        string containerId,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteCommandAsync(context, $"diff {QuotePositionalArgument(containerId, nameof(containerId))}", cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<IList<FilesystemChange>>.Fail(
              ErrorOrDefault(result, "Container diff failed"),
              FailureCode(result.Error, ErrorCodes.Container.DiffFailed),
              CreateErrorContext(context, "DiffContainer", result),
              result.ExitCode);
        }

        var changes = new List<FilesystemChange>();
        var lines = result.Output.Split(LineSeparators, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
          if (line.Length > 2)
          {
            changes.Add(new FilesystemChange
            {
              Kind = line[..1],
              Path = line[2..]
            });
          }
        }

        return CommandResponse<IList<FilesystemChange>>.Ok(changes);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<IList<FilesystemChange>>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Container.DiffFailed));
      }
    }

    #endregion

    #region Helper Types

    /// <summary>DTO for docker ps JSON output.</summary>
    private sealed class DockerPsDto
    {
      public string? ID { get; set; }
      public string? Image { get; set; }
      public string? Command { get; set; }
      public string? CreatedAt { get; set; }
      public string? Names { get; set; }
      public string? State { get; set; }
      public string? Status { get; set; }
      public string? Ports { get; set; }
      public string? Labels { get; set; }
      public string? Mounts { get; set; }
      public string? Networks { get; set; }
      public string? RunningFor { get; set; }
      public string? Size { get; set; }
    }

    #endregion
  }
}
