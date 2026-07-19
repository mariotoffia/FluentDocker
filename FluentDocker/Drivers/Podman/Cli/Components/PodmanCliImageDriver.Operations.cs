using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers.Docker.Cli;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Drivers.Podman.Cli.Components
{
  /// <summary>
  /// Podman CLI image driver — tag, remove, prune, save, load, and import operations.
  /// </summary>
  public partial class PodmanCliImageDriver
  {
    private static readonly char[] LineSeparators = ['\n', '\r'];

    #region Tag/Remove/Prune

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> TagAsync(
        DriverContext context, string imageId, string repository, string tag,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteCommandAsync(
            context,
            $"tag {QuotePositionalArgument(imageId, nameof(imageId))} {QuotePositionalArgument($"{repository}:{tag}", nameof(repository))}", cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<Unit>.Fail(
              ErrorOrDefault(result, "Image tag failed"), FailureCode(result.Error, ErrorCodes.Image.TagFailed),
              CreateErrorContext(context, "TagImage", result), result.ExitCode);

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
        DriverContext context, string imageId, bool force = false, bool noPrune = false,
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
          return CommandResponse<ImageRemoveResult>.Fail(
              ErrorOrDefault(result, "Image remove failed"), FailureCode(result.Error, ErrorCodes.Image.RemoveFailed),
              CreateErrorContext(context, "RemoveImage", result), result.ExitCode);

        return CommandResponse<ImageRemoveResult>.Ok(ParseRemoveOutput(result.Output));
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
        DriverContext context, bool all = false,
        Dictionary<string, string>? filter = null,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = BuildImagePruneArgs(all, filter);

        var result = await ExecuteCommandAsync(context, args, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<ImagePruneResult>.Fail(
              ErrorOrDefault(result, "Image prune failed"), FailureCode(result.Error, ErrorCodes.Image.PruneFailed),
              CreateErrorContext(context, "PruneImages", result), result.ExitCode);

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

    #endregion

    #region Save/Load/Import

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> SaveAsync(
        DriverContext context, string[] images, string outputPath,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = $"save -o {QuoteArgumentIfNeeded(outputPath)} {string.Join(" ", OrEmpty(images).Select(i => QuotePositionalArgument(i, nameof(images))))}";
        var result = await ExecuteUnboundedCommandAsync(context, args, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<Unit>.Fail(
              ErrorOrDefault(result, "Image save failed"), FailureCode(result.Error, ErrorCodes.Image.SaveFailed),
              CreateErrorContext(context, "SaveImage", result), result.ExitCode);

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
        DriverContext context, string inputPath,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteUnboundedCommandAsync(
            context,
            $"load -i {QuoteArgumentIfNeeded(inputPath)}", cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<IList<string>>.Fail(
              ErrorOrDefault(result, "Image load failed"), FailureCode(result.Error, ErrorCodes.Image.LoadFailed),
              CreateErrorContext(context, "LoadImage", result), result.ExitCode);

        var loaded = ParseLoadedImages(result.Output);

        return CommandResponse<IList<string>>.Ok(loaded);
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
        DriverContext context, string source,
        string? repository = null, string? tag = null, string? message = null,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = "import";
        if (!string.IsNullOrEmpty(message))
          args += $" --message {QuoteArgumentIfNeeded(message)}";
        args += $" {QuotePositionalArgument(source, nameof(source))}";
        if (!string.IsNullOrEmpty(repository))
        {
          args += string.IsNullOrEmpty(tag)
              ? $" {QuotePositionalArgument(repository, nameof(repository))}"
              : $" {QuotePositionalArgument($"{repository}:{tag}", nameof(repository))}";
        }

        var result = await ExecuteUnboundedCommandAsync(context, args, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<string>.Fail(
              ErrorOrDefault(result, "Image import failed"), FailureCode(result.Error, ErrorCodes.Image.ImportFailed),
              CreateErrorContext(context, "ImportImage", result), result.ExitCode);

        return CommandResponse<string>.Ok(result.Output?.Trim());
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

    #region Argument Building

    /// <summary>
    /// Builds the CLI arguments string for <c>podman image prune</c>.
    /// </summary>
    public static string BuildImagePruneArgs(bool all, Dictionary<string, string> filter)
    {
      var args = "image prune -f";
      if (all)
        args += " -a";
      if (filter != null)
      {
        foreach (var f in filter)
          args += $" --filter {QuoteArgumentIfNeeded($"{f.Key}={f.Value}")}";
      }
      return args;
    }

    private static IList<string> ParseLoadedImages(string output)
    {
      var images = new List<string>();
      if (string.IsNullOrEmpty(output))
        return images;

      const string loadedPrefix = "Loaded image:";
      const string loadedIdPrefix = "Loaded image ID:";
      const string loadedImagesPrefix = "Loaded image(s):";
      foreach (var line in output.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
      {
        if (line.StartsWith(loadedIdPrefix, StringComparison.OrdinalIgnoreCase))
          images.Add(line[loadedIdPrefix.Length..].Trim());
        else if (line.StartsWith(loadedImagesPrefix, StringComparison.OrdinalIgnoreCase))
        {
          // Legacy podman (<=4.0) comma-joins multiple refs on this one line.
          foreach (var name in line[loadedImagesPrefix.Length..].Split(','))
          {
            var trimmed = name.Trim();
            if (trimmed.Length > 0)
              images.Add(trimmed);
          }
        }
        else if (line.StartsWith(loadedPrefix, StringComparison.OrdinalIgnoreCase))
          images.Add(line[loadedPrefix.Length..].Trim());
      }

      return images;
    }

    private static ImageRemoveResult ParseRemoveOutput(string output)
    {
      var result = new ImageRemoveResult();
      if (string.IsNullOrEmpty(output))
        return result;

      foreach (var line in output.Split(LineSeparators, StringSplitOptions.RemoveEmptyEntries))
      {
        if (line.StartsWith("Deleted:", StringComparison.Ordinal))
          result.Deleted.Add(line[8..].Trim());
        else if (line.StartsWith("Untagged:", StringComparison.Ordinal))
          result.Untagged.Add(line[9..].Trim());
      }

      return result;
    }

    #endregion
  }
}
