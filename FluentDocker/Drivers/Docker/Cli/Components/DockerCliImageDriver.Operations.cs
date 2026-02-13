using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Drivers.Docker.Cli.Components
{
  /// <summary>
  /// Docker CLI image driver — tag, remove, prune, save, load, and import operations.
  /// </summary>
  public partial class DockerCliImageDriver
  {
    #region Tag/Remove Operations

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> TagAsync(
        DriverContext context,
        string imageId,
        string repository,
        string tag,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteCommandAsync($"tag {imageId} {repository}:{tag}", cancellationToken);

        if (!result.Success)
        {
          return CommandResponse<Unit>.Fail(
              result.Error ?? "Image tag failed",
              ErrorCodes.Image.TagFailed);
        }

        return CommandResponse<Unit>.Ok(Unit.Default);
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Image.TagFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<ImageRemoveResult>> RemoveAsync(
        DriverContext context,
        string imageId,
        bool force = false,
        bool noPrune = false,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = "rmi";
        if (force)
          args += " -f";
        if (noPrune)
          args += " --no-prune";
        args += $" {imageId}";

        var result = await ExecuteCommandAsync(args, cancellationToken);

        if (!result.Success)
        {
          return CommandResponse<ImageRemoveResult>.Fail(
              result.Error ?? "Image remove failed",
              ErrorCodes.Image.RemoveFailed);
        }

        // Parse removed/untagged images from output
        var removeResult = new ImageRemoveResult();
        var lines = result.Output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
          if (line.StartsWith("Deleted:"))
            removeResult.Deleted.Add(line.Substring(8).Trim());
          else if (line.StartsWith("Untagged:"))
            removeResult.Untagged.Add(line.Substring(9).Trim());
        }

        return CommandResponse<ImageRemoveResult>.Ok(removeResult);
      }
      catch (Exception ex)
      {
        return CommandResponse<ImageRemoveResult>.Fail(ex.Message, ErrorCodes.Image.RemoveFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<ImagePruneResult>> PruneAsync(
        DriverContext context,
        bool all = false,
        Dictionary<string, string> filter = null,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = "image prune -f";
        if (all)
          args += " -a";
        if (filter != null)
          foreach (var f in filter)
            args += $" --filter {f.Key}={f.Value}";

        var result = await ExecuteCommandAsync(args, cancellationToken);

        if (!result.Success)
        {
          return CommandResponse<ImagePruneResult>.Fail(
              result.Error ?? "Image prune failed",
              ErrorCodes.Image.PruneFailed,
              CreateErrorContext(context, "PruneImages", result),
              result.ExitCode);
        }

        return CommandResponse<ImagePruneResult>.Ok(
            CliPruneOutputParser.ParseImagePruneOutput(result.Output));
      }
      catch (Exception ex)
      {
        return CommandResponse<ImagePruneResult>.Fail(ex.Message, ErrorCodes.Image.PruneFailed);
      }
    }

    #endregion

    #region Save/Load/Import Operations

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> SaveAsync(
        DriverContext context,
        string[] images,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteCommandAsync($"save -o \"{outputPath}\" {string.Join(" ", images)}", cancellationToken);

        if (!result.Success)
        {
          return CommandResponse<Unit>.Fail(
              result.Error ?? "Image save failed",
              ErrorCodes.Image.SaveFailed,
              CreateErrorContext(context, "SaveImage", result),
              result.ExitCode);
        }

        return CommandResponse<Unit>.Ok(Unit.Default);
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Image.SaveFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<IList<string>>> LoadAsync(
        DriverContext context,
        string inputPath,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteCommandAsync($"load -i \"{inputPath}\"", cancellationToken);

        if (!result.Success)
        {
          return CommandResponse<IList<string>>.Fail(
              result.Error ?? "Image load failed",
              ErrorCodes.Image.LoadFailed,
              CreateErrorContext(context, "LoadImage", result),
              result.ExitCode);
        }

        // Parse loaded image names from output
        var images = new List<string>();
        var lines = result.Output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
          if (line.Contains("Loaded image:"))
          {
            var parts = line.Split(':');
            if (parts.Length > 1)
              images.Add(parts[1].Trim());
          }
        }

        return CommandResponse<IList<string>>.Ok(images);
      }
      catch (Exception ex)
      {
        return CommandResponse<IList<string>>.Fail(ex.Message, ErrorCodes.Image.LoadFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<string>> ImportAsync(
        DriverContext context,
        string source,
        string repository = null,
        string tag = null,
        string message = null,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = "import";
        if (!string.IsNullOrEmpty(message))
          args += $" -m \"{message}\"";
        args += $" \"{source}\"";
        if (!string.IsNullOrEmpty(repository))
        {
          args += $" {repository}";
          if (!string.IsNullOrEmpty(tag))
            args += $":{tag}";
        }

        var result = await ExecuteCommandAsync(args, cancellationToken);

        if (!result.Success)
        {
          return CommandResponse<string>.Fail(
              result.Error ?? "Image import failed",
              ErrorCodes.Image.ImportFailed,
              CreateErrorContext(context, "ImportImage", result),
              result.ExitCode);
        }

        return CommandResponse<string>.Ok(result.Output.Trim());
      }
      catch (Exception ex)
      {
        return CommandResponse<string>.Fail(ex.Message, ErrorCodes.Image.ImportFailed);
      }
    }

    #endregion
  }
}
