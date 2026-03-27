using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers.Docker.Cli.Binary;
using FluentDocker.Model.Drivers;

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
        IProgress<ImagePullProgress> progress = null,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var fullImage = string.IsNullOrEmpty(tag) ? image : $"{image}:{tag}";
        var result = await ExecuteCommandAsync($"pull {fullImage}", cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<Unit>.Fail(
              result.Error ?? "Image pull failed",
              ErrorCodes.Image.PullFailed,
              CreateErrorContext(context, "PullImage", result),
              result.ExitCode);
        }

        return CommandResponse<Unit>.Ok(Unit.Default);
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Image.PullFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> PushAsync(
        DriverContext context,
        string image,
        IProgress<ImagePushProgress> progress = null,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteCommandAsync($"push {image}", cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<Unit>.Fail(
              result.Error ?? "Image push failed",
              ErrorCodes.Image.PushFailed,
              CreateErrorContext(context, "PushImage", result),
              result.ExitCode);
        }

        return CommandResponse<Unit>.Ok(Unit.Default);
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Image.PushFailed);
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
        args.Add($"--file {config.DockerfileName}");

      foreach (var tag in config.Tags)
        args.Add($"--tag {tag}");

      foreach (var buildArg in config.BuildArgs)
        args.Add($"--build-arg {buildArg.Key}={buildArg.Value}");

      foreach (var label in config.Labels)
        args.Add($"--label {label.Key}={label.Value}");

      if (!string.IsNullOrEmpty(config.Target))
        args.Add($"--target {config.Target}");

      if (config.NoCache)
        args.Add("--no-cache");

      if (config.Pull)
        args.Add("--pull");

      if (config.ForceRm)
        args.Add("--force-rm");

      if (!string.IsNullOrEmpty(config.Platform))
        args.Add($"--platform {config.Platform}");

      if (!string.IsNullOrEmpty(config.NetworkMode))
        args.Add($"--network {config.NetworkMode}");

      if (!string.IsNullOrEmpty(iidFilePath))
        args.Add($"--iidfile \"{iidFilePath}\"");

      args.Add(config.BuildContext ?? ".");

      return string.Join(" ", args);
    }

    /// <inheritdoc />
    public async Task<CommandResponse<ImageBuildResult>> BuildAsync(
        DriverContext context,
        ImageBuildConfig config,
        IProgress<ImageBuildProgress> progress = null,
        CancellationToken cancellationToken = default)
    {
      try
      {
        // Write image ID to a temp file for deterministic extraction.
        // Both legacy builder and BuildKit honour --iidfile.
        var iidFile = Path.Combine(Path.GetTempPath(), $"docker-iid-{Guid.NewGuid():N}");

        try
        {
          var result = await ExecuteCommandAsync(BuildBuildArgs(config, iidFile), cancellationToken).ConfigureAwait(false);

          if (!result.Success)
          {
            return CommandResponse<ImageBuildResult>.Fail(
                result.Error ?? "Image build failed",
                ErrorCodes.Image.BuildFailed,
                CreateErrorContext(context, "BuildImage", result),
                result.ExitCode);
          }

          var imageId = File.Exists(iidFile)
              ? (await File.ReadAllTextAsync(iidFile, cancellationToken)).Trim()
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
          if (File.Exists(iidFile))
            File.Delete(iidFile);
        }
      }
      catch (Exception ex)
      {
        return CommandResponse<ImageBuildResult>.Fail(ex.Message, ErrorCodes.Image.BuildFailed);
      }
    }

    #endregion

    #region List/Inspect Operations

    /// <inheritdoc />
    public async Task<CommandResponse<IList<Image>>> ListAsync(
        DriverContext context,
        ImageListFilter filter = null,
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
            args.Append($" --filter reference={filter.Reference}");

          if (filter.Dangling.HasValue)
            args.Append($" --filter dangling={(filter.Dangling.Value ? "true" : "false")}");

          if (!string.IsNullOrEmpty(filter.Before))
            args.Append($" --filter before={filter.Before}");

          if (!string.IsNullOrEmpty(filter.Since))
            args.Append($" --filter since={filter.Since}");

          if (filter.Labels != null)
          {
            foreach (var label in filter.Labels)
            {
              var labelValue = string.IsNullOrEmpty(label.Value)
                  ? label.Key
                  : $"{label.Key}={label.Value}";
              args.Append($" --filter label={labelValue}");
            }
          }
        }

        var result = await ExecuteCommandAsync(args.ToString(), cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<IList<Image>>.Fail(
              result.Error ?? "Image list failed",
              ErrorCodes.General.Unknown);
        }

        var images = new List<Image>();
        var lines = result.Output.Split(LineSeparators, StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
          try
          {
            // docker images JSON has Repository, Tag, ID fields
            // We need to map to Image class with RepoTags
            var dto = JsonSerializer.Deserialize<DockerImageDto>(line, JsonHelper.CaseInsensitiveOptions);
            if (dto != null)
            {
              var image = new Image
              {
                Id = dto.ID,
                Size = ParseSize(dto.Size),
                VirtualSize = ParseSize(dto.VirtualSize),
                Containers = int.TryParse(dto.Containers, out var count) ? count : 0
              };

              // Construct RepoTags from Repository and Tag
              if (!string.IsNullOrEmpty(dto.Repository) && !string.IsNullOrEmpty(dto.Tag))
              {
                image.RepoTags.Add($"{dto.Repository}:{dto.Tag}");
              }

              // Parse CreatedAt if present
              if (!string.IsNullOrEmpty(dto.CreatedAt) && DateTime.TryParse(dto.CreatedAt, out var created))
              {
                image.Created = created;
              }

              images.Add(image);
            }
          }
          catch (Exception ex)
          {
            Logger.Log($"Image list JSON parsing failed: {ex.Message}");
          }
        }

        return CommandResponse<IList<Image>>.Ok(images);
      }
      catch (Exception ex)
      {
        return CommandResponse<IList<Image>>.Fail(ex.Message, ErrorCodes.General.Unknown);
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

    /// <summary>
    /// Parses size string like "1.05GB", "125MB", "9.18MB" to bytes.
    /// </summary>
    private static long ParseSize(string sizeStr)
    {
      if (string.IsNullOrEmpty(sizeStr))
        return 0;

      sizeStr = sizeStr.Trim();
      if (sizeStr == "N/A" || sizeStr == "0B")
        return 0;

      var multipliers = new Dictionary<string, long>
            {
                { "B", 1L },
                { "KB", 1024L },
                { "MB", 1024L * 1024 },
                { "GB", 1024L * 1024 * 1024 },
                { "TB", 1024L * 1024 * 1024 * 1024 }
            };

      foreach (var unit in multipliers.Keys)
      {
        if (sizeStr.EndsWith(unit, StringComparison.OrdinalIgnoreCase))
        {
          var numberPart = sizeStr.Substring(0, sizeStr.Length - unit.Length).Trim();
          if (double.TryParse(numberPart, out var number))
          {
            return (long)(number * multipliers[unit]);
          }
        }
      }

      return 0;
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Image>> InspectAsync(
        DriverContext context,
        string imageId,
        CancellationToken cancellationToken = default)
    {
      try
      {
        var result = await ExecuteCommandAsync($"image inspect {imageId}", cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<Image>.Fail(
              result.Error ?? "Image inspect failed",
              ErrorCodes.Image.InspectFailed);
        }

        var images = JsonSerializer.Deserialize<List<Image>>(result.Output, JsonHelper.CaseInsensitiveOptions);
        var image = images?.FirstOrDefault();

        if (image == null)
        {
          return CommandResponse<Image>.Fail(
              $"Image {imageId} not found",
              ErrorCodes.Image.NotFound);
        }

        return CommandResponse<Image>.Ok(image);
      }
      catch (Exception ex)
      {
        return CommandResponse<Image>.Fail(ex.Message, ErrorCodes.Image.InspectFailed);
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
        var result = await ExecuteCommandAsync($"history --format \"{{{{json .}}}}\" --no-trunc {imageId}", cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<IList<ImageLayer>>.Fail(
              result.Error ?? "Image history failed",
              ErrorCodes.Image.HistoryFailed,
              CreateErrorContext(context, "HistoryImage", result),
              result.ExitCode);
        }

        var layers = new List<ImageLayer>();
        var lines = result.Output.Split(LineSeparators, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
          try
          {
            // Docker history JSON has ID, CreatedAt, CreatedBy, Size, Comment fields
            var dto = JsonSerializer.Deserialize<DockerHistoryDto>(line, JsonHelper.CaseInsensitiveOptions);
            if (dto != null)
            {
              var layer = new ImageLayer
              {
                Id = dto.ID,
                CreatedBy = dto.CreatedBy,
                Comment = dto.Comment,
                Size = ParseSize(dto.Size)
              };

              // Parse CreatedAt if present
              if (!string.IsNullOrEmpty(dto.CreatedAt) && DateTime.TryParse(dto.CreatedAt, out var created))
              {
                layer.Created = created;
              }

              layers.Add(layer);
            }
          }
          catch (Exception ex)
          {
            Logger.Log($"Image history JSON parsing failed: {ex.Message}");
          }
        }

        return CommandResponse<IList<ImageLayer>>.Ok(layers);
      }
      catch (Exception ex)
      {
        return CommandResponse<IList<ImageLayer>>.Fail(ex.Message, ErrorCodes.Image.HistoryFailed);
      }
    }

    #endregion
  }
}
