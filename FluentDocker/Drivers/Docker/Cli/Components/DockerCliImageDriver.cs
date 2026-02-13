using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers.Docker.Cli.Binary;
using FluentDocker.Model.Drivers;
using Newtonsoft.Json;

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
        var result = await ExecuteCommandAsync($"pull {fullImage}", cancellationToken);

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
        var result = await ExecuteCommandAsync($"push {image}", cancellationToken);

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

    /// <inheritdoc />
    public async Task<CommandResponse<ImageBuildResult>> BuildAsync(
        DriverContext context,
        ImageBuildConfig config,
        IProgress<ImageBuildProgress> progress = null,
        CancellationToken cancellationToken = default)
    {
      try
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

        args.Add(config.BuildContext ?? ".");

        var result = await ExecuteCommandAsync(string.Join(" ", args), cancellationToken);

        if (!result.Success)
        {
          return CommandResponse<ImageBuildResult>.Fail(
              result.Error ?? "Image build failed",
              ErrorCodes.Image.BuildFailed,
              CreateErrorContext(context, "BuildImage", result),
              result.ExitCode);
        }

        // Extract image ID from build output (usually last line with "sha256:...").
        // Modern BuildKit writes progress/output to stderr, so check both streams.
        var imageId = ExtractImageId(result.Output) ?? ExtractImageId(result.Error) ?? "";

        return CommandResponse<ImageBuildResult>.Ok(new ImageBuildResult
        {
          ImageId = imageId,
          Warnings = new List<string>()
        });
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

        var result = await ExecuteCommandAsync(args.ToString(), cancellationToken);

        if (!result.Success)
        {
          return CommandResponse<IList<Image>>.Fail(
              result.Error ?? "Image list failed",
              ErrorCodes.General.Unknown);
        }

        var images = new List<Image>();
        var lines = result.Output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
          try
          {
            // docker images JSON has Repository, Tag, ID fields
            // We need to map to Image class with RepoTags
            var dto = JsonConvert.DeserializeObject<DockerImageDto>(line);
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
          catch
          {
            // Skip invalid JSON lines
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
    private class DockerImageDto
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
    private class DockerHistoryDto
    {
      public string ID { get; set; }
      public string CreatedBy { get; set; }
      public string CreatedAt { get; set; }
      public string CreatedSince { get; set; }
      public string Size { get; set; }
      public string Comment { get; set; }
    }

    /// <summary>
    /// Scans build output lines (from the end) for a sha256 image ID.
    /// Returns the ID or null if not found.
    /// </summary>
    private static string ExtractImageId(string output)
    {
      if (string.IsNullOrEmpty(output))
        return null;

      var lines = output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
      for (var i = lines.Length - 1; i >= 0; i--)
      {
        var line = lines[i];
        if (!line.Contains("sha256:"))
          continue;

        var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var id = parts.Where(p => p.StartsWith("sha256:")).LastOrDefault();
        if (id != null)
          return id;
      }

      return null;
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
        var result = await ExecuteCommandAsync($"image inspect {imageId}", cancellationToken);

        if (!result.Success)
        {
          return CommandResponse<Image>.Fail(
              result.Error ?? "Image inspect failed",
              ErrorCodes.Image.InspectFailed);
        }

        var images = JsonConvert.DeserializeObject<List<Image>>(result.Output);
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
        var result = await ExecuteCommandAsync($"history --format \"{{{{json .}}}}\" --no-trunc {imageId}", cancellationToken);

        if (!result.Success)
        {
          return CommandResponse<IList<ImageLayer>>.Fail(
              result.Error ?? "Image history failed",
              ErrorCodes.Image.HistoryFailed,
              CreateErrorContext(context, "HistoryImage", result),
              result.ExitCode);
        }

        var layers = new List<ImageLayer>();
        var lines = result.Output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
          try
          {
            // Docker history JSON has ID, CreatedAt, CreatedBy, Size, Comment fields
            var dto = JsonConvert.DeserializeObject<DockerHistoryDto>(line);
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
          catch
          {
            // Skip invalid JSON lines
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
