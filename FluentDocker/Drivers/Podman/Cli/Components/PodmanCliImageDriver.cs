using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers.Docker.Cli;
using FluentDocker.Drivers.Podman.Cli.Binary;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Images;
using Newtonsoft.Json.Linq;

namespace FluentDocker.Drivers.Podman.Cli.Components
{
    /// <summary>
    /// Podman CLI implementation of IImageDriver.
    /// Supports Containerfile (alias for Dockerfile) and uses Buildah under the hood.
    /// </summary>
    public class PodmanCliImageDriver : PodmanCliDriverBase, IImageDriver
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
                var result = await ExecuteCommandAsync($"pull {imageRef}", cancellationToken);
                return result.Success
                    ? CommandResponse<Unit>.Ok(Unit.Default)
                    : CommandResponse<Unit>.Fail(
                        result.Error ?? "Image pull failed", ErrorCodes.Image.PullFailed);
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
                var result = await ExecuteCommandAsync($"push {image}", cancellationToken);
                return result.Success
                    ? CommandResponse<Unit>.Ok(Unit.Default)
                    : CommandResponse<Unit>.Fail(
                        result.Error ?? "Image push failed", ErrorCodes.Image.PushFailed);
            }
            catch (Exception ex)
            {
                return CommandResponse<Unit>.Fail(ex.Message, ErrorCodes.Image.PushFailed);
            }
        }

        #endregion

        #region Build

        /// <inheritdoc />
        public async Task<CommandResponse<ImageBuildResult>> BuildAsync(
            DriverContext context, ImageBuildConfig config,
            IProgress<ImageBuildProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var args = "build";

                foreach (var tag in config.Tags)
                    args += $" -t {tag}";

                if (!string.IsNullOrEmpty(config.DockerfileName))
                    args += $" -f {config.DockerfileName}";
                if (config.NoCache) args += " --no-cache";
                if (config.Pull) args += " --pull";
                if (config.Rm) args += " --rm";
                if (config.ForceRm) args += " --force-rm";
                if (config.Squash) args += " --squash";

                if (!string.IsNullOrEmpty(config.Target))
                    args += $" --target {config.Target}";
                if (!string.IsNullOrEmpty(config.Platform))
                    args += $" --platform {config.Platform}";
                if (!string.IsNullOrEmpty(config.NetworkMode))
                    args += $" --network {config.NetworkMode}";

                foreach (var buildArg in config.BuildArgs)
                    args += $" --build-arg {buildArg.Key}={buildArg.Value}";
                foreach (var label in config.Labels)
                    args += $" --label {label.Key}={label.Value}";

                args += $" {config.BuildContext ?? "."}";

                var result = await ExecuteCommandAsync(args, cancellationToken);
                if (!result.Success)
                    return CommandResponse<ImageBuildResult>.Fail(
                        result.Error ?? "Image build failed", ErrorCodes.Image.BuildFailed);

                return CommandResponse<ImageBuildResult>.Ok(new ImageBuildResult
                {
                    Output = new List<string>(
                        result.Output?.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                        ?? Array.Empty<string>())
                });
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
                if (filter?.All == true) args += " -a";
                if (!string.IsNullOrEmpty(filter?.Reference))
                    args += $" --filter reference={filter.Reference}";

                var result = await ExecuteCommandAsync(args, cancellationToken);
                if (!result.Success)
                    return CommandResponse<IList<Image>>.Fail(
                        result.Error ?? "Image list failed", ErrorCodes.General.Unknown);

                var images = ParseImageList(result.Output);
                return CommandResponse<IList<Image>>.Ok(images);
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
                    $"image inspect {imageId}", cancellationToken);
                if (!result.Success)
                    return CommandResponse<Image>.Fail(
                        result.Error ?? "Image inspect failed", ErrorCodes.Image.InspectFailed);

                var image = ParseImageInspect(result.Output);
                return CommandResponse<Image>.Ok(image);
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
                    $"history --format json {imageId}", cancellationToken);
                if (!result.Success)
                    return CommandResponse<IList<ImageLayer>>.Fail(
                        result.Error ?? "Image history failed", ErrorCodes.Image.HistoryFailed);

                var layers = ParseHistory(result.Output);
                return CommandResponse<IList<ImageLayer>>.Ok(layers);
            }
            catch (Exception ex)
            {
                return CommandResponse<IList<ImageLayer>>.Fail(ex.Message, ErrorCodes.Image.HistoryFailed);
            }
        }

        #endregion

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
                if (force) args += " -f";
                if (noPrune) args += " --no-prune";
                args += $" {imageId}";

                var result = await ExecuteCommandAsync(args, cancellationToken);
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
                var args = "image prune -f";
                if (all) args += " -a";

                var result = await ExecuteCommandAsync(args, cancellationToken);
                if (!result.Success)
                    return CommandResponse<ImagePruneResult>.Fail(
                        result.Error ?? "Image prune failed", ErrorCodes.Image.PruneFailed);

                return CommandResponse<ImagePruneResult>.Ok(new ImagePruneResult());
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
                var args = $"save -o {outputPath} {string.Join(" ", images)}";
                var result = await ExecuteCommandAsync(args, cancellationToken);
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
                var result = await ExecuteCommandAsync($"load -i {inputPath}", cancellationToken);
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
                if (!string.IsNullOrEmpty(message)) args += $" --message \"{message}\"";
                args += $" {source}";
                if (!string.IsNullOrEmpty(repository))
                {
                    args += string.IsNullOrEmpty(tag) ? $" {repository}" : $" {repository}:{tag}";
                }

                var result = await ExecuteCommandAsync(args, cancellationToken);
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

        #region JSON Parsing

        private static IList<Image> ParseImageList(string json)
        {
            var images = new List<Image>();
            if (string.IsNullOrWhiteSpace(json)) return images;

            try
            {
                var trimmed = json.Trim();
                if (trimmed.StartsWith("["))
                {
                    var arr = JArray.Parse(trimmed);
                    foreach (var token in arr)
                        images.Add(ParseImageFromToken(token));
                }
                else
                {
                    foreach (var line in trimmed.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                        images.Add(ParseImageFromToken(JObject.Parse(line.Trim())));
                }
            }
            catch { /* Return partial results */ }

            return images;
        }

        private static Image ParseImageFromToken(JToken token)
        {
            var image = new Image
            {
                Id = token["Id"]?.Value<string>() ?? token["ID"]?.Value<string>(),
                Size = token["Size"]?.Value<long>() ?? 0,
                VirtualSize = token["VirtualSize"]?.Value<long>() ?? 0
            };

            var tags = token["Names"] ?? token["RepoTags"];
            if (tags is JArray tagArray)
                foreach (var t in tagArray)
                    image.RepoTags.Add(t.Value<string>());

            var digests = token["RepoDigests"] ?? token["Digests"];
            if (digests is JArray digestArray)
                foreach (var d in digestArray)
                    image.RepoDigests.Add(d.Value<string>());

            return image;
        }

        private static Image ParseImageInspect(string json)
        {
            try
            {
                var trimmed = json.Trim();
                JToken token;
                if (trimmed.StartsWith("["))
                    token = JArray.Parse(trimmed).First;
                else
                    token = JObject.Parse(trimmed);

                var image = new Image
                {
                    Id = token["Id"]?.Value<string>(),
                    Architecture = token["Architecture"]?.Value<string>(),
                    Os = token["Os"]?.Value<string>(),
                    Size = token["Size"]?.Value<long>() ?? 0,
                    VirtualSize = token["VirtualSize"]?.Value<long>() ?? 0
                };

                var tags = token["RepoTags"];
                if (tags is JArray tagArray)
                    foreach (var t in tagArray)
                        image.RepoTags.Add(t.Value<string>());

                return image;
            }
            catch
            {
                return new Image();
            }
        }

        private static IList<ImageLayer> ParseHistory(string json)
        {
            var layers = new List<ImageLayer>();
            if (string.IsNullOrWhiteSpace(json)) return layers;

            try
            {
                var trimmed = json.Trim();
                JArray arr;
                if (trimmed.StartsWith("["))
                    arr = JArray.Parse(trimmed);
                else
                    return layers;

                foreach (var token in arr)
                {
                    layers.Add(new ImageLayer
                    {
                        Id = token["id"]?.Value<string>() ?? token["Id"]?.Value<string>(),
                        CreatedBy = token["createdBy"]?.Value<string>() ?? token["CreatedBy"]?.Value<string>(),
                        Size = token["size"]?.Value<long>() ?? token["Size"]?.Value<long>() ?? 0,
                        Comment = token["comment"]?.Value<string>() ?? token["Comment"]?.Value<string>()
                    });
                }
            }
            catch { /* Return partial results */ }

            return layers;
        }

        #endregion
    }
}
