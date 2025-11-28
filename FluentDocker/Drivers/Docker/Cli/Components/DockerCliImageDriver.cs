using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Model.Drivers;
using Newtonsoft.Json;

namespace FluentDocker.Drivers.Docker.Cli.Components
{
    /// <summary>
    /// Docker CLI implementation of IImageDriver.
    /// </summary>
    public class DockerCliImageDriver : DockerCliDriverBase, IImageDriver
    {
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

                // Extract image ID from build output (usually last line with "sha256:...")
                var lines = result.Output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                var imageId = "";
                for (var i = lines.Length - 1; i >= 0; i--)
                {
                    var line = lines[i];
                    if (line.Contains("sha256:"))
                    {
                        var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        imageId = parts.Where(p => p.StartsWith("sha256:")).LastOrDefault() ?? "";
                        break;
                    }
                }

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
                var args = "images --format \"{{json .}}\"";

                if (filter?.All == true)
                    args += " -a";

                var result = await ExecuteCommandAsync(args, cancellationToken);

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
                        var image = JsonConvert.DeserializeObject<Image>(line);
                        if (image != null)
                            images.Add(image);
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
                var result = await ExecuteCommandAsync($"history --format '{{{{json .}}}}' --no-trunc {imageId}", cancellationToken);

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
                        var layer = JsonConvert.DeserializeObject<ImageLayer>(line);
                        if (layer != null)
                            layers.Add(layer);
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

                return CommandResponse<ImagePruneResult>.Ok(new ImagePruneResult());
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

