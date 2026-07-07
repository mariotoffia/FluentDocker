using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers.Docker.Cli;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Drivers.Docker.Cli.Components
{
  /// <summary>
  /// Docker CLI image driver — tag, remove, prune, save, load, and import operations.
  /// </summary>
  public partial class DockerCliImageDriver
  {
    private static readonly char[] LineSeparators = ['\n', '\r'];
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
        var result = await ExecuteCommandAsync(context, $"tag {QuotePositionalArgument(imageId, nameof(imageId))} {QuotePositionalArgument($"{repository}:{tag}", nameof(repository))}", cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<Unit>.Fail(
              ErrorOrDefault(result, "Image tag failed"),
              FailureCode(result.Error, ErrorCodes.Image.TagFailed));
        }

        return CommandResponse<Unit>.Ok(Unit.Default);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Image.TagFailed));
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
        args += $" {QuotePositionalArgument(imageId, nameof(imageId))}";

        var result = await ExecuteCommandAsync(context, args, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<ImageRemoveResult>.Fail(
              ErrorOrDefault(result, "Image remove failed"),
              MapRemoveErrorCode(result.Error));
        }

        // Parse removed/untagged images from output
        var removeResult = new ImageRemoveResult();
        var lines = result.Output.Split(LineSeparators, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
          if (line.StartsWith("Deleted:", StringComparison.Ordinal))
            removeResult.Deleted.Add(line[8..].Trim());
          else if (line.StartsWith("Untagged:", StringComparison.Ordinal))
            removeResult.Untagged.Add(line[9..].Trim());
        }

        return CommandResponse<ImageRemoveResult>.Ok(removeResult);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<ImageRemoveResult>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Image.RemoveFailed));
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
            args += $" --filter {QuoteArgumentIfNeeded($"{f.Key}={f.Value}")}";

        var result = await ExecuteCommandAsync(context, args, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<ImagePruneResult>.Fail(
              ErrorOrDefault(result, "Image prune failed"),
              FailureCode(result.Error, ErrorCodes.Image.PruneFailed),
              CreateErrorContext(context, "PruneImages", result),
              result.ExitCode);
        }

        return CommandResponse<ImagePruneResult>.Ok(
            CliPruneOutputParser.ParseImagePruneOutput(result.Output));
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<ImagePruneResult>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Image.PruneFailed));
      }
    }

    private static string MapRemoveErrorCode(string error)
    {
      if (!string.IsNullOrEmpty(error) &&
          error.Contains("No such image", StringComparison.OrdinalIgnoreCase))
        return ErrorCodes.Image.NotFound;
      return FailureCode(error, ErrorCodes.Image.RemoveFailed);
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
        var result = await ExecuteUnboundedCommandAsync(context, $"save -o {QuoteArgumentIfNeeded(outputPath)} {string.Join(" ", images.Select(i => QuotePositionalArgument(i, nameof(images))))}", cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<Unit>.Fail(
              ErrorOrDefault(result, "Image save failed"),
              FailureCode(result.Error, ErrorCodes.Image.SaveFailed),
              CreateErrorContext(context, "SaveImage", result),
              result.ExitCode);
        }

        return CommandResponse<Unit>.Ok(Unit.Default);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Image.SaveFailed));
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
        var result = await ExecuteUnboundedCommandAsync(context, $"load -i {QuoteArgumentIfNeeded(inputPath)}", cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<IList<string>>.Fail(
              ErrorOrDefault(result, "Image load failed"),
              FailureCode(result.Error, ErrorCodes.Image.LoadFailed),
              CreateErrorContext(context, "LoadImage", result),
              result.ExitCode);
        }

        // Parse loaded image names from output.
        // Output format: "Loaded image: nginx:latest" or "Loaded image ID: sha256:abc..."
        // Use substring instead of Split(':') to preserve the tag after the colon.
        var images = new List<string>();
        var lines = result.Output.Split(LineSeparators, StringSplitOptions.RemoveEmptyEntries);
        const string loadedPrefix = "Loaded image:";
        const string loadedIdPrefix = "Loaded image ID:";
        foreach (var line in lines)
        {
          if (line.StartsWith(loadedIdPrefix, StringComparison.OrdinalIgnoreCase))
          {
            images.Add(line[loadedIdPrefix.Length..].Trim());
          }
          else if (line.StartsWith(loadedPrefix, StringComparison.OrdinalIgnoreCase))
          {
            images.Add(line[loadedPrefix.Length..].Trim());
          }
        }

        return CommandResponse<IList<string>>.Ok(images);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<IList<string>>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Image.LoadFailed));
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
          args += $" -m {QuoteArgumentIfNeeded(message)}";
        args += $" {QuotePositionalArgument(source, nameof(source))}";
        if (!string.IsNullOrEmpty(repository))
          args += $" {QuotePositionalArgument(string.IsNullOrEmpty(tag) ? repository : $"{repository}:{tag}", nameof(repository))}";

        var result = await ExecuteUnboundedCommandAsync(context, args, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<string>.Fail(
              ErrorOrDefault(result, "Image import failed"),
              FailureCode(result.Error, ErrorCodes.Image.ImportFailed),
              CreateErrorContext(context, "ImportImage", result),
              result.ExitCode);
        }

        return CommandResponse<string>.Ok(result.Output.Trim());
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<string>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Image.ImportFailed));
      }
    }

    #endregion
  }
}
