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
    #region Tag/Remove/Prune

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> TagAsync(
        DriverContext context, string imageId, string repository, string tag,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteCommandAsync(
            $"tag {imageId} {repository}:{tag}", cancellationToken);
        return result.Success
            ? CommandResponse<Unit>.Ok(Unit.Default)
            : CommandResponse<Unit>.Fail(
                result.Error ?? "Image tag failed", ErrorCodes.Image.TagFailed);
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Image.TagFailed);
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
        args += $" {imageId}";

        var result = await ExecuteCommandAsync(args, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<ImageRemoveResult>.Fail(
              result.Error ?? "Image remove failed", ErrorCodes.Image.RemoveFailed);

        return CommandResponse<ImageRemoveResult>.Ok(new ImageRemoveResult());
      }
      catch (Exception ex)
      {
        return CommandResponse<ImageRemoveResult>.Fail(ex.Message, ErrorCodes.Image.RemoveFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<ImagePruneResult>> PruneAsync(
        DriverContext context, bool all = false,
        Dictionary<string, string> filter = null,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = BuildImagePruneArgs(all, filter);

        var result = await ExecuteCommandAsync(args, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<ImagePruneResult>.Fail(
              result.Error ?? "Image prune failed", ErrorCodes.Image.PruneFailed);

        return CommandResponse<ImagePruneResult>.Ok(
            CliPruneOutputParser.ParseImagePruneOutput(result.Output));
      }
      catch (Exception ex)
      {
        return CommandResponse<ImagePruneResult>.Fail(ex.Message, ErrorCodes.Image.PruneFailed);
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
        var args = $"save -o {QuoteArgumentIfNeeded(outputPath)} {string.Join(" ", images.Select(QuoteArgumentIfNeeded))}";
        var result = await ExecuteCommandAsync(args, cancellationToken).ConfigureAwait(false);
        return result.Success
            ? CommandResponse<Unit>.Ok(Unit.Default)
            : CommandResponse<Unit>.Fail(
                result.Error ?? "Image save failed", ErrorCodes.Image.SaveFailed);
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Image.SaveFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<IList<string>>> LoadAsync(
        DriverContext context, string inputPath,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteCommandAsync(
            $"load -i {QuoteArgumentIfNeeded(inputPath)}", cancellationToken);
        if (!result.Success)
          return CommandResponse<IList<string>>.Fail(
              result.Error ?? "Image load failed", ErrorCodes.Image.LoadFailed);

        var loaded = new List<string>();
        if (!string.IsNullOrEmpty(result.Output))
          loaded.Add(result.Output.Trim());

        return CommandResponse<IList<string>>.Ok(loaded);
      }
      catch (Exception ex)
      {
        return CommandResponse<IList<string>>.Fail(ex.Message, ErrorCodes.Image.LoadFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<string>> ImportAsync(
        DriverContext context, string source,
        string repository = null, string tag = null, string message = null,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = "import";
        if (!string.IsNullOrEmpty(message))
          args += $" --message \"{message}\"";
        args += $" {QuoteArgumentIfNeeded(source)}";
        if (!string.IsNullOrEmpty(repository))
        {
          args += string.IsNullOrEmpty(tag) ? $" {repository}" : $" {repository}:{tag}";
        }

        var result = await ExecuteCommandAsync(args, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<string>.Fail(
              result.Error ?? "Image import failed", ErrorCodes.Image.ImportFailed);

        return CommandResponse<string>.Ok(result.Output?.Trim());
      }
      catch (Exception ex)
      {
        return CommandResponse<string>.Fail(ex.Message, ErrorCodes.Image.ImportFailed);
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
          args += $" --filter {f.Key}={f.Value}";
      }
      return args;
    }

    #endregion
  }
}
