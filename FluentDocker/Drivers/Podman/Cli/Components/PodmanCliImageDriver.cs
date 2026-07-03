using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers.Docker.Cli;
using FluentDocker.Drivers.Podman.Cli.Binary;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Images;

namespace FluentDocker.Drivers.Podman.Cli.Components
{
  /// <summary>
  /// Podman CLI implementation of IImageDriver.
  /// Supports Containerfile (alias for Dockerfile) and uses Buildah under the hood.
  /// </summary>
  public partial class PodmanCliImageDriver : PodmanCliDriverBase, IImageDriver
  {
    public PodmanCliImageDriver(IPodmanBinaryResolver binaryResolver) : base(binaryResolver)
    {
    }

    #region Pull/Push

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> PullAsync(
        DriverContext context, string image, string tag = "latest",
        IProgress<ImagePullProgress> progress = null,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var imageRef = string.IsNullOrEmpty(tag) ? image : $"{image}:{tag}";
        var result = await ExecuteUnboundedCommandAsync(
            context,
            $"pull {QuotePositionalArgument(imageRef, nameof(image))}", cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<Unit>.Fail(
              ErrorOrDefault(result, "Image pull failed"), ErrorCodes.Image.PullFailed,
              CreateErrorContext(context, "Pull", result), result.ExitCode);

        return CommandResponse<Unit>.Ok(Unit.Default);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Image.PullFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> PushAsync(
        DriverContext context, string image,
        IProgress<ImagePushProgress> progress = null,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteUnboundedCommandAsync(
            context,
            $"push {QuotePositionalArgument(image, nameof(image))}", cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<Unit>.Fail(
              ErrorOrDefault(result, "Image push failed"), ErrorCodes.Image.PushFailed,
              CreateErrorContext(context, "Push", result), result.ExitCode);

        return CommandResponse<Unit>.Ok(Unit.Default);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Image.PushFailed);
      }
    }

    #endregion

    #region Build

    /// <summary>
    /// Builds the CLI arguments for <c>podman build</c>.
    /// The <paramref name="iidFilePath"/> is appended via <c>--iidfile</c>
    /// so Podman writes the image ID to a deterministic file.
    /// </summary>
    public static string BuildBuildArgs(ImageBuildConfig config, string iidFilePath)
    {
      var args = "build";

      foreach (var tag in config.Tags)
        args += $" -t {QuoteArgumentIfNeeded(tag)}";

      if (!string.IsNullOrEmpty(config.DockerfileName))
        args += $" -f {QuoteArgumentIfNeeded(config.DockerfileName)}";
      if (config.NoCache)
        args += " --no-cache";
      if (config.Pull)
        args += " --pull";
      if (config.Rm)
        args += " --rm";
      if (config.ForceRm)
        args += " --force-rm";
      if (config.Squash)
        args += " --squash";

      if (!string.IsNullOrEmpty(config.Target))
        args += $" --target {QuoteArgumentIfNeeded(config.Target)}";
      if (!string.IsNullOrEmpty(config.Platform))
        args += $" --platform {QuoteArgumentIfNeeded(config.Platform)}";
      if (!string.IsNullOrEmpty(config.NetworkMode))
        args += $" --network {QuoteArgumentIfNeeded(config.NetworkMode)}";

      foreach (var buildArg in config.BuildArgs)
        args += $" --build-arg {QuoteArgumentIfNeeded($"{buildArg.Key}={buildArg.Value}")}";
      foreach (var label in config.Labels)
        args += $" --label {QuoteArgumentIfNeeded($"{label.Key}={label.Value}")}";

      if (!string.IsNullOrEmpty(iidFilePath))
        args += $" --iidfile {QuoteArgumentIfNeeded(iidFilePath)}";

      args += $" {QuotePositionalArgument(config.BuildContext ?? ".", nameof(config.BuildContext))}";

      return args;
    }

    /// <inheritdoc />
    public async Task<CommandResponse<ImageBuildResult>> BuildAsync(
        DriverContext context, ImageBuildConfig config,
        IProgress<ImageBuildProgress> progress = null,
        CancellationToken cancellationToken = default)
    {
      try
      {
        // Write image ID to a temp file for deterministic extraction.
        // Podman (via Buildah) honours --iidfile.
        var iidFile = Path.Combine(Path.GetTempPath(), $"podman-iid-{Guid.NewGuid():N}");

        try
        {
          var result = await ExecuteUnboundedCommandAsync(context, BuildBuildArgs(config, iidFile), cancellationToken).ConfigureAwait(false);
          if (!result.Success)
            return CommandResponse<ImageBuildResult>.Fail(
                ErrorOrDefault(result, "Image build failed"), ErrorCodes.Image.BuildFailed,
                CreateErrorContext(context, "Build", result), result.ExitCode);

          var imageId = File.Exists(iidFile)
              ? (await File.ReadAllTextAsync(iidFile, cancellationToken)).Trim()
              : null;

          if (string.IsNullOrEmpty(imageId))
          {
            return CommandResponse<ImageBuildResult>.Fail(
                "Build succeeded but image ID could not be determined",
                ErrorCodes.Image.BuildFailed);
          }

          return CommandResponse<ImageBuildResult>.Ok(new ImageBuildResult
          {
            ImageId = imageId,
            Output = new List<string>(
                  result.Output?.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                  ?? Array.Empty<string>())
          });
        }
        finally
        {
          if (File.Exists(iidFile))
            File.Delete(iidFile);
        }
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<ImageBuildResult>.Fail(ex.Message, ErrorCodes.Image.BuildFailed);
      }
    }

    #endregion

    #region List/Inspect/History

    /// <inheritdoc />
    public async Task<CommandResponse<IList<Image>>> ListAsync(
        DriverContext context, ImageListFilter filter = null,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = "images --format json";
        if (filter?.All == true)
          args += " -a";
        if (!string.IsNullOrEmpty(filter?.Reference))
          args += $" --filter {QuoteArgumentIfNeeded($"reference={filter.Reference}")}";

        var result = await ExecuteCommandAsync(context, args, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<IList<Image>>.Fail(
              ErrorOrDefault(result, "Image list failed"), ErrorCodes.General.Unknown,
              CreateErrorContext(context, "ListImages", result), result.ExitCode);

        var images = ParseImageList(result.Output);
        return CommandResponse<IList<Image>>.Ok(images);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<IList<Image>>.Fail(ex.Message, ErrorCodes.General.Unknown);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Image>> InspectAsync(
        DriverContext context, string imageId,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteCommandAsync(
            context,
            $"image inspect {QuotePositionalArgument(imageId, nameof(imageId))}", cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<Image>.Fail(
              ErrorOrDefault(result, "Image inspect failed"), ErrorCodes.Image.InspectFailed,
              CreateErrorContext(context, "InspectImage", result), result.ExitCode);

        var image = ParseImageInspect(result.Output);
        if (string.IsNullOrEmpty(image.Id))
          return CommandResponse<Image>.Fail(
              $"Image '{imageId}' was not found", ErrorCodes.Image.NotFound,
              CreateErrorContext(context, "InspectImage", result), result.ExitCode);
        return CommandResponse<Image>.Ok(image);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<Image>.Fail(ex.Message, ErrorCodes.Image.InspectFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<IList<ImageLayer>>> HistoryAsync(
        DriverContext context, string imageId,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteCommandAsync(
            context,
            $"history --format json {QuotePositionalArgument(imageId, nameof(imageId))}", cancellationToken).ConfigureAwait(false);
        if (!result.Success)
          return CommandResponse<IList<ImageLayer>>.Fail(
              ErrorOrDefault(result, "Image history failed"), ErrorCodes.Image.HistoryFailed,
              CreateErrorContext(context, "ImageHistory", result), result.ExitCode);

        var layers = ParseHistory(result.Output);
        return CommandResponse<IList<ImageLayer>>.Ok(layers);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<IList<ImageLayer>>.Fail(ex.Message, ErrorCodes.Image.HistoryFailed);
      }
    }

    #endregion

    #region JSON Parsing

    private static List<Image> ParseImageList(string json)
    {
      var images = new List<Image>();
      if (string.IsNullOrWhiteSpace(json))
        return images;

      try
      {
        var trimmed = json.Trim();
        if (trimmed.StartsWith('['))
        {
          var root = JsonHelper.ParseElement(trimmed);
          foreach (var token in root.EnumerateArraySafe())
            images.Add(ParseImageFromToken(token));
        }
        else
        {
          foreach (var line in trimmed.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            images.Add(ParseImageFromToken(JsonHelper.ParseElement(line.Trim())));
        }
      }
      catch (Exception ex)
      {
        // Non-empty but unparseable output is a real failure — surface it with diagnostics
        // instead of silently returning a partial/empty list that masks the problem.
        throw new FluentDockerException(
            $"Failed to parse Podman image list output: {ex.Message}");
      }

      return images;
    }

    private static Image ParseImageFromToken(JsonElement token)
    {
      var image = new Image
      {
        Id = token.GetStringOrDefault("Id") ?? token.GetStringOrDefault("ID"),
        Size = token.GetInt64OrDefault("Size"),
        VirtualSize = token.GetInt64OrDefault("VirtualSize")
      };

      var tagsProp = token.Prop("Names") ?? token.Prop("RepoTags");
      if (tagsProp.HasValue && tagsProp.Value.ValueKind == JsonValueKind.Array)
        foreach (var t in tagsProp.Value.EnumerateArray())
          image.RepoTags.Add(t.GetString());

      var digestsProp = token.Prop("RepoDigests") ?? token.Prop("Digests");
      if (digestsProp.HasValue && digestsProp.Value.ValueKind == JsonValueKind.Array)
        foreach (var d in digestsProp.Value.EnumerateArray())
          image.RepoDigests.Add(d.GetString());

      return image;
    }

    private static Image ParseImageInspect(string json)
    {
      if (string.IsNullOrWhiteSpace(json))
        return new Image();

      try
      {
        var trimmed = json.Trim();
        JsonElement token;
        if (trimmed.StartsWith('['))
        {
          var root = JsonHelper.ParseElement(trimmed);
          using var enumerator = root.EnumerateArray();
          if (!enumerator.MoveNext())
            return new Image();
          token = enumerator.Current;
        }
        else
        {
          token = JsonHelper.ParseElement(trimmed);
        }

        var image = new Image
        {
          Id = token.GetStringOrDefault("Id"),
          Architecture = token.GetStringOrDefault("Architecture"),
          Os = token.GetStringOrDefault("Os"),
          Size = token.GetInt64OrDefault("Size"),
          VirtualSize = token.GetInt64OrDefault("VirtualSize")
        };

        var tagsProp = token.Prop("RepoTags");
        if (tagsProp.HasValue && tagsProp.Value.ValueKind == JsonValueKind.Array)
          foreach (var t in tagsProp.Value.EnumerateArray())
            image.RepoTags.Add(t.GetString());

        return image;
      }
      catch (Exception ex)
      {
        throw new FluentDockerException(
            $"Failed to parse Podman image inspect output: {ex.Message}");
      }
    }

    private static List<ImageLayer> ParseHistory(string json)
    {
      var layers = new List<ImageLayer>();
      if (string.IsNullOrWhiteSpace(json))
        return layers;

      try
      {
        var trimmed = json.Trim();
        if (!trimmed.StartsWith('['))
          throw new FormatException("expected a JSON array");

        var root = JsonHelper.ParseElement(trimmed);
        foreach (var token in root.EnumerateArraySafe())
        {
          layers.Add(new ImageLayer
          {
            Id = token.GetStringOrDefault("id") ?? token.GetStringOrDefault("Id"),
            CreatedBy = token.GetStringOrDefault("createdBy")
                        ?? token.GetStringOrDefault("CreatedBy"),
            Size = token.GetInt64OrDefault("size", token.GetInt64OrDefault("Size")),
            Comment = token.GetStringOrDefault("comment")
                      ?? token.GetStringOrDefault("Comment")
          });
        }
      }
      catch (Exception ex)
      {
        throw new FluentDockerException(
            $"Failed to parse Podman image history output: {ex.Message}");
      }

      return layers;
    }

    #endregion
  }
}
