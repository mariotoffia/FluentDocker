using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Model.Drivers;
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
        var result = await ExecuteCommandAsync($"inspect {QuoteArgumentIfNeeded(containerId)}", cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<Container>.Fail(
              result.Error ?? "Container inspect failed",
              ErrorCodes.Container.InspectFailed,
              CreateErrorContext(context, "InspectContainer", result),
              result.ExitCode);
        }

        var containers = JsonSerializer.Deserialize<List<Container>>(result.Output, JsonHelper.CaseInsensitiveOptions);
        var container = containers?.FirstOrDefault();

        if (container == null)
        {
          return CommandResponse<Container>.Fail(
              $"Container {containerId} not found",
              ErrorCodes.Container.NotFound);
        }

        return CommandResponse<Container>.Ok(container);
      }
      catch (Exception ex)
      {
        return CommandResponse<Container>.Fail(ex.Message, ErrorCodes.Container.InspectFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<IList<Container>>> ListAsync(
        DriverContext context,
        ContainerListFilter filter = null,
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
              args += $" --filter \"label={label.Key}\"";
            else
              args += $" --filter \"label={label.Key}={label.Value}\"";
          }
        }

        // Add name filter
        if (!string.IsNullOrEmpty(filter?.Name))
          args += $" --filter \"name={filter.Name}\"";

        // Add status filter
        if (!string.IsNullOrEmpty(filter?.Status))
          args += $" --filter \"status={filter.Status}\"";

        // Add ID filter
        if (!string.IsNullOrEmpty(filter?.Id))
          args += $" --filter \"id={filter.Id}\"";

        // Add ancestor filter
        if (!string.IsNullOrEmpty(filter?.Ancestor))
          args += $" --filter \"ancestor={filter.Ancestor}\"";

        var result = await ExecuteCommandAsync(args, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<IList<Container>>.Fail(
              result.Error ?? "Container list failed",
              ErrorCodes.General.Unknown);
        }

        var containers = new List<Container>();
        var lines = result.Output.Split(LineSeparators, StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
          try
          {
            // Docker ps JSON has different field names than our Container model
            var dto = JsonSerializer.Deserialize<DockerPsDto>(line, JsonHelper.CaseInsensitiveOptions);
            if (dto != null)
            {
              var container = new Container
              {
                Id = dto.ID,
                Image = dto.Image,
                Name = dto.Names
              };

              // Parse CreatedAt if present
              if (!string.IsNullOrEmpty(dto.CreatedAt) && DateTime.TryParse(dto.CreatedAt, out var created))
              {
                container.Created = created;
              }

              // Parse State if present
              if (!string.IsNullOrEmpty(dto.State))
              {
                container.State = new ContainerState
                {
                  Running = dto.State.Equals("running", StringComparison.OrdinalIgnoreCase),
                  Status = dto.Status
                };
              }

              containers.Add(container);
            }
          }
          catch (Exception ex)
          {
            Logger.Log($"Container inspect JSON parsing failed: {ex.Message}");
          }
        }

        return CommandResponse<IList<Container>>.Ok(containers);
      }
      catch (Exception ex)
      {
        return CommandResponse<IList<Container>>.Fail(ex.Message, ErrorCodes.General.Unknown);
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
          throw new NotSupportedException(
              "GetLogsAsync does not support follow=true because 'docker logs -f' " +
              "streams indefinitely. Use IStreamDriver.StreamLogsAsync instead.");
        }

        var args = "logs";
        if (tail.HasValue)
          args += $" --tail {tail.Value}";
        if (timestamps)
          args += " -t";
        args += $" {QuoteArgumentIfNeeded(containerId)}";

        var result = await ExecuteCommandAsync(args, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<string>.Fail(
              result.Error ?? "Get logs failed",
              ErrorCodes.Container.LogsFailed);
        }

        // docker logs writes to both stdout and stderr.
        // Combine both to capture all container output.
        var logs = !string.IsNullOrEmpty(result.Error)
            ? result.Output + result.Error
            : result.Output;
        return CommandResponse<string>.Ok(logs);
      }
      catch (Exception ex)
      {
        return CommandResponse<string>.Fail(ex.Message, ErrorCodes.General.Unknown);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<ContainerProcesses>> TopAsync(
        DriverContext context,
        string containerId,
        string psOptions = null,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = $"top {QuoteArgumentIfNeeded(containerId)}";
        if (!string.IsNullOrEmpty(psOptions))
          args += $" {psOptions}";

        var result = await ExecuteCommandAsync(args, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<ContainerProcesses>.Fail(
              result.Error ?? "Container top failed",
              ErrorCodes.Container.TopFailed,
              CreateErrorContext(context, "TopContainer", result),
              result.ExitCode);
        }

        var processes = new ContainerProcesses();
        var lines = result.Output.Split(LineSeparators, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length > 0)
        {
          processes.Titles = lines[0].Split(SpaceSeparator, StringSplitOptions.RemoveEmptyEntries).ToList();
          for (var i = 1; i < lines.Length; i++)
          {
            processes.Processes.Add(lines[i].Split(SpaceSeparator, StringSplitOptions.RemoveEmptyEntries).ToList());
          }
        }

        return CommandResponse<ContainerProcesses>.Ok(processes);
      }
      catch (Exception ex)
      {
        return CommandResponse<ContainerProcesses>.Fail(ex.Message, ErrorCodes.Container.TopFailed);
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
        var result = await ExecuteCommandAsync($"diff {QuoteArgumentIfNeeded(containerId)}", cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<IList<FilesystemChange>>.Fail(
              result.Error ?? "Container diff failed",
              ErrorCodes.Container.DiffFailed,
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
              Kind = line.Substring(0, 1),
              Path = line.Substring(2)
            });
          }
        }

        return CommandResponse<IList<FilesystemChange>>.Ok(changes);
      }
      catch (Exception ex)
      {
        return CommandResponse<IList<FilesystemChange>>.Fail(ex.Message, ErrorCodes.Container.DiffFailed);
      }
    }

    #endregion

    #region Helper Types

    /// <summary>DTO for docker ps JSON output.</summary>
    private sealed class DockerPsDto
    {
      public string ID { get; set; }
      public string Image { get; set; }
      public string Command { get; set; }
      public string CreatedAt { get; set; }
      public string Names { get; set; }
      public string State { get; set; }
      public string Status { get; set; }
      public string Ports { get; set; }
      public string Labels { get; set; }
      public string Mounts { get; set; }
      public string Networks { get; set; }
      public string RunningFor { get; set; }
      public string Size { get; set; }
    }

    #endregion
  }
}
