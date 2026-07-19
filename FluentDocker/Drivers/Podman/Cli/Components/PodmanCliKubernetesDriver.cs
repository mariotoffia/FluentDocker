using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers.Podman.Cli.Binary;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Drivers.Podman.Cli.Components
{
  /// <summary>
  /// Podman CLI implementation of Kubernetes YAML operations.
  /// Supports <c>podman kube play</c>, <c>podman kube down</c>,
  /// and <c>podman kube generate</c>.
  /// </summary>
  public class PodmanCliKubernetesDriver : PodmanCliDriverBase, IPodmanKubernetesDriver
  {
    /// <summary>Creates a new instance with the specified binary resolver.</summary>
    public PodmanCliKubernetesDriver(IPodmanBinaryResolver binaryResolver)
        : base(binaryResolver)
    {
    }

    #region Operations

    /// <inheritdoc />
    public async Task<CommandResponse<KubePlayResult>> PlayAsync(
        DriverContext context, KubePlayConfig config,
        CancellationToken cancellationToken = default)
    {
      ArgumentNullException.ThrowIfNull(config);
      if (string.IsNullOrWhiteSpace(config.YamlPath))
        throw new ArgumentException("YamlPath is required", nameof(config));

      try
      {
        var args = BuildPlayArgs(config);
        var result = await ExecuteUnboundedCommandAsync(context, args, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<KubePlayResult>.Fail(
              ErrorOrDefault(result, "Kube play failed"),
              FailureCode(result.Error, ErrorCodes.Kubernetes.PlayFailed),
              CreateErrorContext(context, "KubePlay", result), result.ExitCode);
        }

        var playResult = ParsePlayOutput(result.Output);
        return CommandResponse<KubePlayResult>.Ok(playResult);
      }
      catch (OperationCanceledException)
      {
        // `podman kube play` may already have created pods/infra containers before the
        // cancellation killed it, and no handle is returned to the caller — tear them down
        // best-effort so a cancelled play does not leak running pods (mirrors the cidfile
        // cleanup on cancelled container runs).
        await TryKubeDownOnCancellationAsync(context, config.YamlPath).ConfigureAwait(false);
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<KubePlayResult>.Fail(
            ex.Message, FailureCode(ex, ErrorCodes.Kubernetes.PlayFailed));
      }
    }

    /// <summary>
    /// Best-effort <c>kube down</c> for a cancelled <c>kube play</c>. Failures are swallowed:
    /// cleanup must never mask the caller's cancellation.
    /// </summary>
    private async Task TryKubeDownOnCancellationAsync(DriverContext context, string yamlPath)
    {
      try
      {
        // ponytail: 5s cleanup budget on cancel; raise if slow daemons legitimately need longer.
        using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await ExecuteCommandAsync(
            context,
            $"kube down {QuotePositionalArgument(yamlPath, nameof(yamlPath))}",
            cleanupCts.Token).ConfigureAwait(false);
      }
      catch
      {
        // Best-effort only.
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<Unit>> DownAsync(
        DriverContext context, string yamlPath,
        CancellationToken cancellationToken = default)
    {
      if (string.IsNullOrWhiteSpace(yamlPath))
        throw new ArgumentException("yamlPath is required", nameof(yamlPath));

      try
      {
        var result = await ExecuteUnboundedCommandAsync(
            context,
            $"kube down {QuotePositionalArgument(yamlPath, nameof(yamlPath))}", cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<Unit>.Fail(
              ErrorOrDefault(result, "Kube down failed"),
              FailureCode(result.Error, ErrorCodes.Kubernetes.DownFailed),
              CreateErrorContext(context, "KubeDown", result), result.ExitCode);
        }

        return CommandResponse<Unit>.Ok(Unit.Default);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<Unit>.Fail(
            ex.Message, FailureCode(ex, ErrorCodes.Kubernetes.DownFailed));
      }
    }

    /// <inheritdoc />
    public async Task<CommandResponse<string>> GenerateAsync(
        DriverContext context, string resourceName,
        CancellationToken cancellationToken = default)
    {
      if (string.IsNullOrWhiteSpace(resourceName))
        throw new ArgumentException("resourceName is required", nameof(resourceName));

      try
      {
        var result = await ExecuteCommandAsync(
            context,
            $"kube generate {QuotePositionalArgument(resourceName, nameof(resourceName))}", cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<string>.Fail(
              ErrorOrDefault(result, "Kube generate failed"),
              FailureCode(result.Error, ErrorCodes.Kubernetes.GenerateFailed),
              CreateErrorContext(context, "KubeGenerate", result), result.ExitCode);
        }

        return CommandResponse<string>.Ok(result.Output?.TrimEnd() ?? string.Empty);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<string>.Fail(
            ex.Message, FailureCode(ex, ErrorCodes.Kubernetes.GenerateFailed));
      }
    }

    #endregion

    #region Argument Building

    internal static string BuildPlayArgs(KubePlayConfig config)
    {
      var args = "kube play";

      if (!string.IsNullOrEmpty(config.Network))
        args += $" --network {QuoteArgumentIfNeeded(config.Network)}";

      foreach (var cm in OrEmpty(config.ConfigMaps))
        args += $" --configmap {QuoteArgumentIfNeeded(cm)}";

      if (!string.IsNullOrEmpty(config.LogDriver))
        args += $" --log-driver {QuoteArgumentIfNeeded(config.LogDriver)}";

      if (config.Replace)
        args += " --replace";

      if (!config.Start)
        args += " --start=false";

      foreach (var annotation in OrEmpty(config.Annotations))
        args += $" --annotation {QuoteArgumentIfNeeded($"{annotation.Key}={annotation.Value}")}";

      args += $" {QuotePositionalArgument(config.YamlPath, nameof(config.YamlPath))}";

      return args;
    }

    #endregion

    #region Output Parsing

    /// <summary>
    /// Parses the output of <c>podman kube play</c>.
    /// Output format varies by Podman version:
    /// - Structured JSON (newer versions)
    /// - Line-based Pod/Container IDs (older versions)
    /// </summary>
    internal static KubePlayResult ParsePlayOutput(string output)
    {
      var result = new KubePlayResult();
      if (string.IsNullOrWhiteSpace(output))
        return result;

      try
      {
        var trimmed = output.Trim();

        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
          return ParsePlayOutputJson(trimmed);

        return ParsePlayOutputLines(trimmed);
      }
      catch (Exception ex)
      {
        throw new FluentDockerException(
            $"Failed to parse Podman kube play output: {ex.Message}");
      }
    }

    private static KubePlayResult ParsePlayOutputJson(string json)
    {
      var result = new KubePlayResult();

      var token = JsonHelper.ParseElement(json);

      // Handle {"Pods": [...]} format
      if (token.ValueKind == JsonValueKind.Object)
      {
        var podsToken = token.Prop("Pods") ?? token.Prop("pods");
        if (podsToken.HasValue && podsToken.Value.ValueKind == JsonValueKind.Array)
        {
          foreach (var pod in podsToken.Value.EnumerateArray())
            result.Pods.Add(ParsePodResultFromToken(pod));
        }
        else
        {
          // Single pod object
          result.Pods.Add(ParsePodResultFromToken(token));
        }
      }
      else if (token.ValueKind == JsonValueKind.Array)
      {
        foreach (var pod in token.EnumerateArray())
          result.Pods.Add(ParsePodResultFromToken(pod));
      }

      return result;
    }

    private static KubePlayPodResult ParsePodResultFromToken(JsonElement token)
    {
      var pod = new KubePlayPodResult
      {
        Id = token.GetStringOrDefault("ID")
             ?? token.GetStringOrDefault("Id")
             ?? token.GetStringOrDefault("id")
      };

      var containers = token.Prop("Containers") ?? token.Prop("containers");
      if (containers.HasValue && containers.Value.ValueKind == JsonValueKind.Array)
      {
        foreach (var c in containers.Value.EnumerateArray())
        {
          string? id;
          if (c.ValueKind == JsonValueKind.String)
            id = c.GetString();
          else
            id = c.GetStringOrDefault("ID")
                 ?? c.GetStringOrDefault("Id")
                 ?? c.GetStringOrDefault("id");

          if (!string.IsNullOrEmpty(id))
            pod.Containers.Add(id);
        }
      }

      return pod;
    }

    private static KubePlayResult ParsePlayOutputLines(string output)
    {
      var result = new KubePlayResult();
      KubePlayPodResult? currentPod = null;
      var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
      string? pendingLabel = null; // "pod" or "container"

      foreach (var line in lines)
      {
        var trimmed = line.Trim();
        if (string.IsNullOrEmpty(trimmed))
          continue;

        if (trimmed.Equals("Pod:", StringComparison.OrdinalIgnoreCase))
        {
          pendingLabel = "pod";
          continue;
        }

        if (trimmed.Equals("Container:", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("Containers:", StringComparison.OrdinalIgnoreCase))
        {
          pendingLabel = "container";
          continue;
        }

        if (trimmed.StartsWith("Pod:", StringComparison.OrdinalIgnoreCase))
        {
          var id = trimmed.Substring(4).Trim();
          currentPod = new KubePlayPodResult { Id = id };
          result.Pods.Add(currentPod);
          pendingLabel = null;
          continue;
        }

        if (trimmed.StartsWith("Container:", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("Containers:", StringComparison.OrdinalIgnoreCase))
        {
          var id = trimmed[(trimmed.IndexOf(':') + 1)..].Trim();
          if (currentPod != null && !string.IsNullOrEmpty(id))
            currentPod.Containers.Add(id);
          pendingLabel = null;
          continue;
        }

        // ID on next line after "Pod:" or "Container:" label
        if (pendingLabel == "pod")
        {
          currentPod = new KubePlayPodResult { Id = trimmed };
          result.Pods.Add(currentPod);
          pendingLabel = null;
        }
        else if (pendingLabel == "container")
        {
          if (currentPod != null && !string.IsNullOrEmpty(trimmed))
            currentPod.Containers.Add(trimmed);
          pendingLabel = null;
        }
        else if (IsHexId(trimmed))
        {
          // Bare ID line — could be pod or container ID
          if (currentPod == null)
          {
            currentPod = new KubePlayPodResult { Id = trimmed };
            result.Pods.Add(currentPod);
          }
          else
          {
            currentPod.Containers.Add(trimmed);
          }
        }
      }

      return result;
    }

    private static bool IsHexId(string value)
    {
      // ponytail: podman kube play prints full 64-char hex IDs; the 64-char guard also blocks
      // hex-looking volume names from being misparsed as bare pod/container IDs. If a podman
      // version ever emits bare 12-char short IDs here, add `|| value.Length == 12`.
      return value.Length == 64
          && value.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'));
    }

    #endregion
  }
}
