using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers.Podman.Cli.Binary;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Drivers.Podman.Cli.Components
{
  /// <summary>
  /// Podman CLI implementation of manifest (multi-arch) operations.
  /// </summary>
  public class PodmanCliManifestDriver : PodmanCliDriverBase, IPodmanManifestDriver
  {
    public PodmanCliManifestDriver(IPodmanBinaryResolver binaryResolver)
        : base(binaryResolver) { }

    #region Lifecycle

    /// <inheritdoc />
    public async Task<CommandResponse<string>> CreateAsync(
        DriverContext context, ManifestCreateConfig config,
        CancellationToken cancellationToken = default)
    {
      ArgumentNullException.ThrowIfNull(config);
      if (string.IsNullOrEmpty(config.Name))
        throw new ArgumentException("Manifest name is required", nameof(config));

      try
      {
        var args = BuildCreateArgs(config);
        var result = await ExecuteCommandAsync(args, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
          return CommandResponse<string>.Fail(
              result.Error ?? "Manifest create failed",
              ErrorCodes.Manifest.CreateFailed, result.ExitCode);

        return CommandResponse<string>.Ok(result.Output?.TrimEnd());
      }
      catch (ArgumentException) { throw; }
      catch (Exception ex)
      {
        return CommandResponse<string>.Fail(
            ex.Message, ErrorCodes.Manifest.CreateFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> RemoveAsync(
        DriverContext context, string listName,
        CancellationToken cancellationToken = default)
    {
      if (string.IsNullOrEmpty(listName))
        throw new ArgumentException("List name is required", nameof(listName));

      try
      {
        var result = await ExecuteCommandAsync(
            $"manifest rm {listName}", cancellationToken);

        if (!result.Success)
          return CommandResponse<Unit>.Fail(
              result.Error ?? "Manifest remove failed",
              ErrorCodes.Manifest.RemoveFailed, result.ExitCode);

        return CommandResponse<Unit>.Ok(Unit.Default);
      }
      catch (ArgumentException) { throw; }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(
            ex.Message, ErrorCodes.Manifest.RemoveFailed);
      }
    }

    #endregion

    #region Content

    /// <inheritdoc />
    public async Task<CommandResponse<string>> AddAsync(
        DriverContext context, ManifestAddConfig config,
        CancellationToken cancellationToken = default)
    {
      ArgumentNullException.ThrowIfNull(config);
      if (string.IsNullOrEmpty(config.ListName))
        throw new ArgumentException("List name is required", nameof(config));
      if (string.IsNullOrEmpty(config.Image))
        throw new ArgumentException("Image is required", nameof(config));

      try
      {
        var args = BuildAddArgs(config);
        var result = await ExecuteCommandAsync(args, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
          return CommandResponse<string>.Fail(
              result.Error ?? "Manifest add failed",
              ErrorCodes.Manifest.AddFailed, result.ExitCode);

        return CommandResponse<string>.Ok(result.Output?.TrimEnd());
      }
      catch (ArgumentException) { throw; }
      catch (Exception ex)
      {
        return CommandResponse<string>.Fail(
            ex.Message, ErrorCodes.Manifest.AddFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> AnnotateAsync(
        DriverContext context, ManifestAnnotateConfig config,
        CancellationToken cancellationToken = default)
    {
      ArgumentNullException.ThrowIfNull(config);
      if (string.IsNullOrEmpty(config.ListName))
        throw new ArgumentException("List name is required", nameof(config));
      if (string.IsNullOrEmpty(config.Image))
        throw new ArgumentException("Image is required", nameof(config));

      try
      {
        var args = BuildAnnotateArgs(config);
        var result = await ExecuteCommandAsync(args, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
          return CommandResponse<Unit>.Fail(
              result.Error ?? "Manifest annotate failed",
              ErrorCodes.Manifest.AnnotateFailed, result.ExitCode);

        return CommandResponse<Unit>.Ok(Unit.Default);
      }
      catch (ArgumentException) { throw; }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(
            ex.Message, ErrorCodes.Manifest.AnnotateFailed);
      }
    }

    #endregion

    #region Distribution

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> PushAsync(
        DriverContext context, ManifestPushConfig config,
        CancellationToken cancellationToken = default)
    {
      ArgumentNullException.ThrowIfNull(config);
      if (string.IsNullOrEmpty(config.ListName))
        throw new ArgumentException("List name is required", nameof(config));
      if (string.IsNullOrEmpty(config.Destination))
        throw new ArgumentException("Destination is required", nameof(config));

      try
      {
        var args = BuildPushArgs(config);
        var result = await ExecuteCommandAsync(args, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
          return CommandResponse<Unit>.Fail(
              result.Error ?? "Manifest push failed",
              ErrorCodes.Manifest.PushFailed, result.ExitCode);

        return CommandResponse<Unit>.Ok(Unit.Default);
      }
      catch (ArgumentException) { throw; }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(
            ex.Message, ErrorCodes.Manifest.PushFailed);
      }
    }

    #endregion

    #region Query

    /// <inheritdoc />
    public async Task<CommandResponse<ManifestInspectResult>> InspectAsync(
        DriverContext context, string listName,
        CancellationToken cancellationToken = default)
    {
      if (string.IsNullOrEmpty(listName))
        throw new ArgumentException("List name is required", nameof(listName));

      try
      {
        var result = await ExecuteCommandAsync(
            $"manifest inspect {listName}", cancellationToken);

        if (!result.Success)
          return CommandResponse<ManifestInspectResult>.Fail(
              result.Error ?? "Manifest inspect failed",
              ErrorCodes.Manifest.InspectFailed, result.ExitCode);

        var parsed = ParseManifestInspect(result.Output);
        return CommandResponse<ManifestInspectResult>.Ok(parsed);
      }
      catch (ArgumentException) { throw; }
      catch (Exception ex)
      {
        return CommandResponse<ManifestInspectResult>.Fail(
            ex.Message, ErrorCodes.Manifest.InspectFailed);
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<bool>> ExistsAsync(
        DriverContext context, string listName,
        CancellationToken cancellationToken = default)
    {
      if (string.IsNullOrEmpty(listName))
        throw new ArgumentException("List name is required", nameof(listName));

      try
      {
        var result = await ExecuteCommandAsync(
            $"manifest exists {listName}", cancellationToken);

        // Exit code 0 = exists, non-zero = does not exist
        return CommandResponse<bool>.Ok(result.Success);
      }
      catch (Exception ex)
      {
        return CommandResponse<bool>.Fail(
            ex.Message, ErrorCodes.Manifest.InspectFailed);
      }
    }

    #endregion

    #region Argument Building

    internal static string BuildCreateArgs(ManifestCreateConfig config)
    {
      var sb = new StringBuilder("manifest create");

      if (config.All)
        sb.Append(" --all");
      if (config.Amend)
        sb.Append(" --amend");

      foreach (var kvp in config.Annotations)
        sb.Append($" --annotation \"{kvp.Key}={kvp.Value}\"");

      sb.Append($" {config.Name}");

      foreach (var image in config.Images)
        sb.Append($" {image}");

      return sb.ToString();
    }

    internal static string BuildAddArgs(ManifestAddConfig config)
    {
      var sb = new StringBuilder("manifest add");

      if (config.All)
        sb.Append(" --all");
      if (!string.IsNullOrEmpty(config.Arch))
        sb.Append($" --arch {config.Arch}");
      if (!string.IsNullOrEmpty(config.Os))
        sb.Append($" --os {config.Os}");
      if (!string.IsNullOrEmpty(config.Variant))
        sb.Append($" --variant {config.Variant}");
      if (!string.IsNullOrEmpty(config.OsVersion))
        sb.Append($" --os-version {config.OsVersion}");

      foreach (var feature in config.Features)
        sb.Append($" --features {feature}");

      foreach (var kvp in config.Annotations)
        sb.Append($" --annotation \"{kvp.Key}={kvp.Value}\"");

      sb.Append($" {config.ListName} {config.Image}");

      return sb.ToString();
    }

    internal static string BuildPushArgs(ManifestPushConfig config)
    {
      var sb = new StringBuilder("manifest push");

      if (config.All)
        sb.Append(" --all");
      if (config.Rm)
        sb.Append(" --rm");
      if (!string.IsNullOrEmpty(config.Format))
        sb.Append($" --format {config.Format}");
      if (config.TlsVerify.HasValue)
        sb.Append($" --tls-verify={config.TlsVerify.Value.ToString().ToLowerInvariant()}");

      sb.Append($" {config.ListName} {config.Destination}");

      return sb.ToString();
    }

    internal static string BuildAnnotateArgs(ManifestAnnotateConfig config)
    {
      var sb = new StringBuilder("manifest annotate");

      if (!string.IsNullOrEmpty(config.Arch))
        sb.Append($" --arch {config.Arch}");
      if (!string.IsNullOrEmpty(config.Os))
        sb.Append($" --os {config.Os}");
      if (!string.IsNullOrEmpty(config.Variant))
        sb.Append($" --variant {config.Variant}");
      if (!string.IsNullOrEmpty(config.OsVersion))
        sb.Append($" --os-version {config.OsVersion}");

      foreach (var feature in config.OsFeatures)
        sb.Append($" --os-features {feature}");

      foreach (var feature in config.Features)
        sb.Append($" --features {feature}");

      if (config.IndexAnnotation)
        sb.Append(" --index");

      foreach (var kvp in config.Annotations)
        sb.Append($" --annotation \"{kvp.Key}={kvp.Value}\"");

      sb.Append($" {config.ListName} {config.Image}");

      return sb.ToString();
    }

    #endregion

    #region JSON Parsing

    internal static ManifestInspectResult ParseManifestInspect(string json)
    {
      var result = new ManifestInspectResult();
      if (string.IsNullOrWhiteSpace(json))
        return result;

      try
      {
        var obj = JsonHelper.ParseElement(json.Trim());

        var svProp = obj.Prop("schemaVersion", "SchemaVersion");
        if (svProp.HasValue && svProp.Value.ValueKind == JsonValueKind.Number)
          result.SchemaVersion = svProp.Value.GetInt32();

        result.MediaType = obj.GetStringOrDefault("mediaType")
                           ?? obj.GetStringOrDefault("MediaType");

        var manifests = obj.Prop("manifests", "Manifests");
        if (manifests.HasValue && manifests.Value.ValueKind == JsonValueKind.Array)
        {
          foreach (var item in manifests.Value.EnumerateArray())
            result.Manifests.Add(ParseManifestEntry(item));
        }
      }
      catch (Exception ex) { Logger.Log($"Manifest JSON parsing skipped: {ex.Message}"); }

      return result;
    }

    private static ManifestEntry ParseManifestEntry(JsonElement token)
    {
      var entry = new ManifestEntry
      {
        MediaType = token.GetStringOrDefault("mediaType")
                    ?? token.GetStringOrDefault("MediaType"),
        Size = token.GetInt64OrDefault("size", token.GetInt64OrDefault("Size")),
        Digest = token.GetStringOrDefault("digest")
                 ?? token.GetStringOrDefault("Digest")
      };

      var platform = token.Prop("platform", "Platform");
      if (platform.HasValue && !platform.Value.IsNullOrUndefined())
      {
        var p = platform.Value;
        entry.Platform = new ManifestPlatform
        {
          Architecture = p.GetStringOrDefault("architecture")
                         ?? p.GetStringOrDefault("Architecture"),
          Os = p.GetStringOrDefault("os")
               ?? p.GetStringOrDefault("Os"),
          Variant = p.GetStringOrDefault("variant")
                    ?? p.GetStringOrDefault("Variant"),
          OsVersion = p.GetStringOrDefault("os.version")
                      ?? p.GetStringOrDefault("OsVersion")
        };

        var features = p.Prop("features", "Features");
        if (features.HasValue && features.Value.ValueKind == JsonValueKind.Array)
        {
          entry.Platform.Features = new List<string>();
          foreach (var f in features.Value.EnumerateArray())
            entry.Platform.Features.Add(f.GetString());
        }
      }

      var annotations = token.Prop("annotations", "Annotations");
      if (annotations.HasValue && annotations.Value.ValueKind == JsonValueKind.Object)
      {
        entry.Annotations = new Dictionary<string, string>();
        foreach (var prop in annotations.Value.EnumerateObject())
          entry.Annotations[prop.Name] = prop.Value.GetString();
      }

      return entry;
    }

    #endregion
  }
}
