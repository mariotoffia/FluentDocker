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
        var result = await ExecuteUnboundedCommandAsync(args, cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<KubePlayResult>.Fail(
              result.Error ?? "Kube play failed",
              ErrorCodes.Kubernetes.PlayFailed,
              CreateErrorContext(context, "KubePlay", result), result.ExitCode);
        }

        var playResult = ParsePlayOutput(result.Output);
        return CommandResponse<KubePlayResult>.Ok(playResult);
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<KubePlayResult>.Fail(
            ex.Message, ErrorCodes.Kubernetes.PlayFailed);
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
            $"kube down {QuoteArgumentIfNeeded(yamlPath)}", cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<Unit>.Fail(
              result.Error ?? "Kube down failed",
              ErrorCodes.Kubernetes.DownFailed,
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
            ex.Message, ErrorCodes.Kubernetes.DownFailed);
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
            $"kube generate {QuoteArgumentIfNeeded(resourceName)}", cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
          return CommandResponse<string>.Fail(
              result.Error ?? "Kube generate failed",
              ErrorCodes.Kubernetes.GenerateFailed,
              CreateErrorContext(context, "KubeGenerate", result), result.ExitCode);
        }

        return CommandResponse<string>.Ok(result.Output?.TrimEnd());
      }
      catch (OperationCanceledException)
      {
        throw;
      }
      catch (Exception ex)
      {
        return CommandResponse<string>.Fail(
            ex.Message, ErrorCodes.Kubernetes.GenerateFailed);
      }
    }

    #endregion

    #region Argument Building

    internal static string BuildPlayArgs(KubePlayConfig config)
    {
      var args = "kube play";

      if (!string.IsNullOrEmpty(config.Network))
        args += $" --network {QuoteArgumentIfNeeded(config.Network)}";

      foreach (var cm in config.ConfigMaps)
        args += $" --configmap {QuoteArgumentIfNeeded(cm)}";

      if (!string.IsNullOrEmpty(config.LogDriver))
        args += $" --log-driver {QuoteArgumentIfNeeded(config.LogDriver)}";

      if (config.Replace)
        args += " --replace";

      if (!config.Start)
        args += " --start=false";

      foreach (var annotation in config.Annotations)
        args += $" --annotation {QuoteArgumentIfNeeded($"{annotation.Key}={annotation.Value}")}";

      args += $" {QuoteArgumentIfNeeded(config.YamlPath)}";

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

        result = ParsePlayOutputLines(trimmed);
        if (result.Pods.Count == 0)
        {
          throw new FormatException("expected pod or container identifiers");
        }

        return result;
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
          string id;
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
      KubePlayPodResult currentPod = null;
      var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
      string pendingLabel = null; // "pod" or "container"

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

        if (trimmed.Equals("Container:", StringComparison.OrdinalIgnoreCase))
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

        if (trimmed.StartsWith("Container:", StringComparison.OrdinalIgnoreCase))
        {
          var id = trimmed.Substring(10).Trim();
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
      return value.Length >= 12
          && value.All(c => char.IsLetterOrDigit(c) && !char.IsUpper(c));
    }

    #endregion
  }
}
