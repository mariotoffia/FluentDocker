using System.Collections.Generic;
using FluentDocker.Executors;
using FluentDocker.Executors.Parsers;
using FluentDocker.Extensions;
using FluentDocker.Model.Commands;
using FluentDocker.Model.Common;
using FluentDocker.Model.Containers;
using FluentDocker.Model.Images;

namespace FluentDocker.Commands
{
  /// <summary>
  /// Docker image commands.
  /// </summary>
  /// <remarks>
  /// This class is deprecated. Use the IImageDriver interface from the FluentDocker.Drivers namespace instead.
  /// The Driver layer provides async operations, better error handling, and support for multiple container runtimes.
  /// </remarks>
  [System.Obsolete("Use IImageDriver from FluentDocker.Drivers namespace instead. Will be removed in v4.0.0.")]
  public static class Images
  {
    #region New struct-based command methods

    /// <summary>
    /// Removes images using command args struct.
    /// </summary>
    public static CommandResponse<IList<DockerRmImageRowResponse>> ImageRmCommand(this DockerUri host, ImageRmCommandArgs args)
    {
      var certArgs = $"{host.RenderBaseArgs(args.Certificates)}";
      var options = args.ToString();
      var imageIds = args.ImageIds != null ? string.Join(" ", args.ImageIds) : "";

      return
        new ProcessExecutor<ImageRmResponseParser, IList<DockerRmImageRowResponse>>(
          "docker".ResolveBinary(),
          $"{certArgs} image rm {options} {imageIds}").Execute();
    }

    /// <summary>
    /// Pulls an image using command args struct.
    /// </summary>
    public static CommandResponse<IList<string>> ImagePullCommand(this DockerUri host, ImagePullCommandArgs args)
    {
      var certArgs = $"{host.RenderBaseArgs(args.Certificates)}";
      var options = args.ToString();

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker".ResolveBinary(),
          $"{certArgs} image pull {options} {args.Image}").Execute();
    }

    /// <summary>
    /// Pushes an image using command args struct.
    /// </summary>
    public static CommandResponse<IList<string>> ImagePushCommand(this DockerUri host, ImagePushCommandArgs args)
    {
      var certArgs = $"{host.RenderBaseArgs(args.Certificates)}";
      var options = args.ToString();

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker".ResolveBinary(),
          $"{certArgs} image push {options} {args.Image}").Execute();
    }

    /// <summary>
    /// Lists images using command args struct.
    /// </summary>
    public static CommandResponse<IList<DockerImageRowResponse>> ImagesListCommand(this DockerUri host, ImagesListCommandArgs args)
    {
      var certArgs = $"{host.RenderBaseArgs(args.Certificates)}";
      var options = args.ToString();

      if (!options.Contains("--format"))
        options += " --format \"{{.ID}};{{.Repository}};{{.Tag}}\"";

      return
        new ProcessExecutor<ClientImagesResponseParser, IList<DockerImageRowResponse>>(
          "docker".ResolveBinary(),
          $"{certArgs} images {options}").Execute();
    }

    /// <summary>
    /// Tags an image using command args struct.
    /// </summary>
    public static CommandResponse<IList<string>> ImageTagCommand(this DockerUri host, ImageTagCommandArgs args)
    {
      var certArgs = $"{host.RenderBaseArgs(args.Certificates)}";

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker".ResolveBinary(),
          $"{certArgs} image tag {args.SourceImage} {args.TargetImage}").Execute();
    }

    /// <summary>
    /// Inspects images using command args struct.
    /// </summary>
    public static CommandResponse<IList<string>> ImageInspectCommand(this DockerUri host, ImageInspectCommandArgs args)
    {
      var certArgs = $"{host.RenderBaseArgs(args.Certificates)}";
      var options = args.ToString();
      var imageIds = args.ImageIds != null ? string.Join(" ", args.ImageIds) : "";

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker".ResolveBinary(),
          $"{certArgs} image inspect {options} {imageIds}").Execute();
    }

    /// <summary>
    /// Shows image history using command args struct.
    /// </summary>
    public static CommandResponse<IList<string>> ImageHistoryCommand(this DockerUri host, ImageHistoryCommandArgs args)
    {
      var certArgs = $"{host.RenderBaseArgs(args.Certificates)}";
      var options = args.ToString();

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker".ResolveBinary(),
          $"{certArgs} image history {options} {args.Image}").Execute();
    }

    /// <summary>
    /// Saves images to tar archive using command args struct.
    /// </summary>
    public static CommandResponse<IList<string>> ImageSaveCommand(this DockerUri host, ImageSaveCommandArgs args)
    {
      var certArgs = $"{host.RenderBaseArgs(args.Certificates)}";
      var options = args.ToString();
      var images = args.Images != null ? string.Join(" ", args.Images) : "";

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker".ResolveBinary(),
          $"{certArgs} image save {options} {images}").Execute();
    }

    /// <summary>
    /// Loads images from tar archive using command args struct.
    /// </summary>
    public static CommandResponse<IList<string>> ImageLoadCommand(this DockerUri host, ImageLoadCommandArgs args)
    {
      var certArgs = $"{host.RenderBaseArgs(args.Certificates)}";
      var options = args.ToString();

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker".ResolveBinary(),
          $"{certArgs} image load {options}").Execute();
    }

    /// <summary>
    /// Prunes unused images using command args struct.
    /// </summary>
    public static CommandResponse<IList<string>> ImagePruneCommand(this DockerUri host, ImagePruneCommandArgs args)
    {
      var certArgs = $"{host.RenderBaseArgs(args.Certificates)}";
      var options = args.ToString();

      return
        new ProcessExecutor<StringListResponseParser, IList<string>>(
          "docker".ResolveBinary(),
          $"{certArgs} image prune {options}").Execute();
    }

    #endregion

    #region Existing methods (backward compatible)

    /// <summary>
    /// Removes one or more images.
    /// </summary>
    public static CommandResponse<IList<DockerRmImageRowResponse>> Rm(
        this DockerUri host,
        ICertificatePaths certificates = null,
        bool force = false, bool prune = true,
        params string[] imageId)
    {
      var options = "";

      if (!prune)
      {
        options = "--no-prune";
      }

      if (force)
      {
        options += " --force";
      }

      return
        new ProcessExecutor<ImageRmResponseParser, IList<DockerRmImageRowResponse>>(
          "docker".ResolveBinary(),
          $"{host.RenderBaseArgs(certificates)} image rm {options} {string.Join(" ", imageId)}").Execute();
    }

    #endregion
  }
}