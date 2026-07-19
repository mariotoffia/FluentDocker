#nullable disable warnings
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers.Docker.Cli.Binary;
using FluentDocker.Model.Drivers;
using Microsoft.Extensions.Logging;
namespace FluentDocker.Drivers.Docker.Cli.Components
{
  /// <summary>
  /// Docker CLI implementation of IImageDriver.
  /// </summary>
  public partial class DockerCliImageDriver : DockerCliDriverBase, IImageDriver
  {
    /// <summary>
    /// Creates a new instance with the specified binary resolver.
    /// </summary>
    public DockerCliImageDriver(IBinaryResolver binaryResolver) : base(binaryResolver)
    {
    }

    #region Pull/Push Operations

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> PullAsync(
        DriverContext context,
        string image,
        string tag = "latest",
        IProgress<ImagePullProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var fullImage = ShouldAppendTag(image, tag) ? $"{image}:{tag}" : image;
        var result = await ExecuteProgressCommandAsync(
            context,
            $"pull {QuotePositionalArgument(fullImage, nameof(image))}",
            progress,
            CreatePullProgress,
            cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<Unit>.Fail(
              ErrorOrDefault(result, "Image pull failed"),
              FailureCode(result.Error, ErrorCodes.Image.PullFailed),
              CreateErrorContext(context, "PullImage", result),
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
        return CommandResponse<Unit>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Image.PullFailed));
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> PushAsync(
        DriverContext context,
        string image,
        IProgress<ImagePushProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteProgressCommandAsync(
            context,
            $"push {QuotePositionalArgument(image, nameof(image))}",
            progress,
            CreatePushProgress,
            cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<Unit>.Fail(
              ErrorOrDefault(result, "Image push failed"),
              FailureCode(result.Error, ErrorCodes.Image.PushFailed),
              CreateErrorContext(context, "PushImage", result),
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
        return CommandResponse<Unit>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Image.PushFailed));
      }
    }

    #endregion

    #region Build Operations

    /// <summary>
    /// Builds the CLI arguments for <c>docker build</c>.
    /// The <paramref name="iidFilePath"/> is appended via <c>--iidfile</c>
    /// so Docker writes the image ID to a deterministic file.
    /// </summary>
    public static string BuildBuildArgs(ImageBuildConfig config, string iidFilePath)
    {
      var args = new List<string> { "build" };

      if (!string.IsNullOrEmpty(config.DockerfileName))
        args.Add($"--file {QuoteArgumentIfNeeded(config.DockerfileName)}");

      foreach (var tag in config.Tags)
        args.Add($"--tag {QuoteArgumentIfNeeded(tag)}");

      foreach (var buildArg in config.BuildArgs)
        args.Add($"--build-arg {QuoteArgumentIfNeeded($"{buildArg.Key}={buildArg.Value}")}");

      foreach (var label in config.Labels)
        args.Add($"--label {QuoteArgumentIfNeeded($"{label.Key}={label.Value}")}");

      if (!string.IsNullOrEmpty(config.Target))
        args.Add($"--target {QuoteArgumentIfNeeded(config.Target)}");

      if (config.NoCache)
        args.Add("--no-cache");

      if (config.Pull)
        args.Add("--pull");

      if (config.ForceRm)
        args.Add("--force-rm");

      if (!string.IsNullOrEmpty(config.Platform))
        args.Add($"--platform {QuoteArgumentIfNeeded(config.Platform)}");

      if (!string.IsNullOrEmpty(config.NetworkMode))
        args.Add($"--network {QuoteArgumentIfNeeded(config.NetworkMode)}");

      if (!string.IsNullOrEmpty(iidFilePath))
        args.Add($"--iidfile {QuoteArgumentIfNeeded(iidFilePath)}");

      args.Add(QuotePositionalArgument(config.BuildContext ?? ".", nameof(config.BuildContext)));

      return string.Join(" ", args);
    }

    private static bool ShouldAppendTag(string image, string tag)
    {
      if (string.IsNullOrEmpty(tag) || string.IsNullOrEmpty(image) || image.Contains('@', StringComparison.Ordinal))
        return false;

      var lastSlash = image.LastIndexOf('/');
      var lastSegment = lastSlash < 0 ? image : image[(lastSlash + 1)..];
      return !lastSegment.Contains(':', StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public async Task<CommandResponse<ImageBuildResult>> BuildAsync(
        DriverContext context,
        ImageBuildConfig config,
        IProgress<ImageBuildProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
      try
      {
        // Write image ID to a temp file for deterministic extraction.
        // Both legacy builder and BuildKit honour --iidfile.
        var iidFile = CreateIidFilePath();

        try
        {
          var result = await ExecuteProgressCommandAsync(
              context,
              BuildBuildArgs(config, iidFile),
              progress,
              CreateBuildProgress,
              cancellationToken).ConfigureAwait(false);

          if (!result.Success)
          {
            return CommandResponse<ImageBuildResult>.Fail(
                ErrorOrDefault(result, "Image build failed"),
                FailureCode(result.Error, ErrorCodes.Image.BuildFailed),
                CreateErrorContext(context, "BuildImage", result),
                result.ExitCode);
          }

          var imageId = File.Exists(iidFile)
              ? (await File.ReadAllTextAsync(iidFile, cancellationToken).ConfigureAwait(false)).Trim()
              : null;

          if (string.IsNullOrEmpty(imageId))
          {
            return CommandResponse<ImageBuildResult>.Fail(
                "Build succeeded but image ID could not be determined",
                ErrorCodes.Image.BuildFailed,
                CreateErrorContext(context, "BuildImage", result),
                result.ExitCode);
          }

          return CommandResponse<ImageBuildResult>.Ok(new ImageBuildResult
          {
            ImageId = imageId,
            Warnings = new List<string>()
          });
        }
        finally
        {
          try
          {
            if (File.Exists(iidFile))
              File.Delete(iidFile);
          }
          catch (IOException)
          {
          }
          catch (UnauthorizedAccessException)
          {
          }
        }
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<ImageBuildResult>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Image.BuildFailed));
      }
    }

    #endregion

    #region List/Inspect Operations

    /// <inheritdoc />
    public async Task<CommandResponse<IList<Image>>> ListAsync(
        DriverContext context,
        ImageListFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var args = new StringBuilder("images --format \"{{json .}}\"");

        if (filter?.All == true)
          args.Append(" -a");

        if (filter != null)
        {
          if (!string.IsNullOrEmpty(filter.Reference))
            args.Append(CultureInfo.InvariantCulture, $" --filter {QuoteArgumentIfNeeded($"reference={filter.Reference}")}");

          if (filter.Dangling.HasValue)
            args.Append(CultureInfo.InvariantCulture, $" --filter {QuoteArgumentIfNeeded($"dangling={(filter.Dangling.Value ? "true" : "false")}")}");

          if (!string.IsNullOrEmpty(filter.Before))
            args.Append(CultureInfo.InvariantCulture, $" --filter {QuoteArgumentIfNeeded($"before={filter.Before}")}");

          if (!string.IsNullOrEmpty(filter.Since))
            args.Append(CultureInfo.InvariantCulture, $" --filter {QuoteArgumentIfNeeded($"since={filter.Since}")}");

          if (filter.Labels != null)
          {
            foreach (var label in filter.Labels)
            {
              var labelValue = string.IsNullOrEmpty(label.Value)
                  ? label.Key
                  : $"{label.Key}={label.Value}";
              args.Append(CultureInfo.InvariantCulture, $" --filter {QuoteArgumentIfNeeded($"label={labelValue}")}");
            }
          }
        }

        var result = await ExecuteCommandAsync(context, args.ToString(), cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<IList<Image>>.Fail(
              ErrorOrDefault(result, "Image list failed"),
              FailureCode(result.Error, ErrorCodes.General.Unknown));
        }

        if (!DockerCliJsonLineParser.TryParse<DockerImageDto>(
            result.Output,
            Logger,
            "Image list JSON parsing failed",
            out var dtos,
            out var parseError))
          return CommandResponse<IList<Image>>.Fail(parseError, ErrorCodes.General.Unknown);

        var images = new List<Image>();
        foreach (var dto in dtos)
        {
          var image = new Image
          {
            Id = dto.ID,
            Size = ParseSize(dto.Size),
            VirtualSize = ParseSize(dto.VirtualSize),
            Containers = int.TryParse(dto.Containers, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) ? count : 0
          };

          if (!string.IsNullOrEmpty(dto.Repository) && !string.IsNullOrEmpty(dto.Tag))
          {
            image.RepoTags.Add($"{dto.Repository}:{dto.Tag}");
          }

          if (DockerCliTimestampParser.TryParse(dto.CreatedAt, out DateTime created))
          {
            image.Created = created;
          }
          else if (!string.IsNullOrEmpty(dto.CreatedAt) && Logger.IsEnabled(LogLevel.Debug))
          {
            Logger.LogDebug("Unparseable image CreatedAt '{CreatedAt}'", dto.CreatedAt);
          }

          images.Add(image);
        }

        return CommandResponse<IList<Image>>.Ok(images);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<IList<Image>>.Fail(ex.Message, FailureCode(ex, ErrorCodes.General.Unknown));
      }
    }

    /// <summary>
    /// DTO for docker images JSON output.
    /// </summary>
    private sealed class DockerImageDto
    {
      public string ID { get; set; }
      public string Repository { get; set; }
      public string Tag { get; set; }
      public string Size { get; set; }
      public string VirtualSize { get; set; }
      public string CreatedAt { get; set; }
      public string Containers { get; set; }
      public string Digest { get; set; }
    }

    /// <summary>
    /// DTO for docker history JSON output.
    /// </summary>
    private sealed class DockerHistoryDto
    {
      public string ID { get; set; }
      public string CreatedBy { get; set; }
      public string CreatedAt { get; set; }
      public string CreatedSince { get; set; }
      public string Size { get; set; }
      public string Comment { get; set; }
    }

    private static long ParseSize(string sizeStr) => CliOutputParser.ParseByteValue(sizeStr);

    /// <inheritdoc />
    public async Task<CommandResponse<Image>> InspectAsync(
        DriverContext context,
        string imageId,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteCommandAsync(context, $"image inspect {QuotePositionalArgument(imageId, nameof(imageId))}", cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<Image>.Fail(
              ErrorOrDefault(result, "Image inspect failed"),
              result.Error?.Contains("No such image", StringComparison.OrdinalIgnoreCase) == true
                  ? ErrorCodes.Image.NotFound
                  : FailureCode(result.Error, ErrorCodes.Image.InspectFailed));
        }

        var images = JsonHelper.TryDeserialize<List<Image>>(result.Output);
        if (images == null)
        {
          Logger.LogError("Image inspect JSON parsing failed");
          return CommandResponse<Image>.Fail(
              "Image inspect JSON parsing failed",
              ErrorCodes.Image.InspectFailed);
        }

        var image = images?.FirstOrDefault();

        if (image == null)
        {
          return CommandResponse<Image>.Fail(
              $"Image {imageId} not found",
              ErrorCodes.Image.NotFound);
        }

        return CommandResponse<Image>.Ok(image);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<Image>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Image.InspectFailed));
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<IList<ImageLayer>>> HistoryAsync(
        DriverContext context,
        string imageId,
        CancellationToken cancellationToken = default)
    {
      try
      {
        // Quote the format string to ensure it's treated as a single argument
        var result = await ExecuteCommandAsync(context, $"history --format \"{{{{json .}}}}\" --no-trunc {QuotePositionalArgument(imageId, nameof(imageId))}", cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<IList<ImageLayer>>.Fail(
              ErrorOrDefault(result, "Image history failed"),
              FailureCode(result.Error, ErrorCodes.Image.HistoryFailed),
              CreateErrorContext(context, "HistoryImage", result),
              result.ExitCode);
        }

        if (!DockerCliJsonLineParser.TryParse<DockerHistoryDto>(
            result.Output,
            Logger,
            "Image history JSON parsing failed",
            out var dtos,
            out var parseError))
          return CommandResponse<IList<ImageLayer>>.Fail(parseError, ErrorCodes.Image.HistoryFailed);

        var layers = new List<ImageLayer>();
        foreach (var dto in dtos)
        {
          var layer = new ImageLayer
          {
            Id = dto.ID,
            CreatedBy = dto.CreatedBy,
            Comment = dto.Comment,
            Size = ParseSize(dto.Size)
          };

          if (DockerCliTimestampParser.TryParse(dto.CreatedAt, out DateTime created))
          {
            layer.Created = created;
          }
          else if (!string.IsNullOrEmpty(dto.CreatedAt) && Logger.IsEnabled(LogLevel.Debug))
          {
            Logger.LogDebug("Unparseable history CreatedAt '{CreatedAt}'", dto.CreatedAt);
          }

          layers.Add(layer);
        }

        return CommandResponse<IList<ImageLayer>>.Ok(layers);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<IList<ImageLayer>>.Fail(ex.Message, FailureCode(ex, ErrorCodes.Image.HistoryFailed));
      }
    }

    #endregion
  }
}
