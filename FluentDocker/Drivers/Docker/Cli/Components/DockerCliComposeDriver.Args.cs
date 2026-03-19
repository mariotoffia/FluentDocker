using System;
using System.Collections.Generic;
using System.Text.Json;
using FluentDocker.Common;

namespace FluentDocker.Drivers.Docker.Cli.Components
{
  /// <summary>
  /// Docker CLI compose driver: public static argument builders for testability.
  /// Each method builds the subcommand-specific args (after compose base args).
  /// </summary>
  public partial class DockerCliComposeDriver
  {
    #region Argument Builders

    /// <summary>
    /// Builds args for <c>docker compose rm</c>.
    /// </summary>
    public static string BuildRemoveSubArgs(ComposeRemoveConfig config)
    {
      var args = "rm";
      if (config.Force)
        args += " -f";
      if (config.Stop)
        args += " -s";
      if (config.Volumes)
        args += " -v";
      return args;
    }

    /// <summary>
    /// Builds args for <c>docker compose ps</c>.
    /// </summary>
    public static string BuildListSubArgs(ComposeListConfig config)
    {
      var args = "ps --format json";
      if (config.All)
        args += " -a";
      if (config.Quiet)
        args += " -q";
      if (!string.IsNullOrEmpty(config.Status))
        args += $" --filter status={config.Status}";
      return args;
    }

    /// <summary>
    /// Builds args for <c>docker compose up</c> (flags only, no services).
    /// </summary>
    public static string BuildUpSubArgs(ComposeUpConfig config)
    {
      var args = "up";
      if (config.Detached)
        args += " -d";
      if (config.Build)
        args += " --build";
      if (config.ForceRecreate)
        args += " --force-recreate";
      if (config.NoRecreate)
        args += " --no-recreate";
      if (config.RemoveOrphans)
        args += " --remove-orphans";
      if (config.NoBuild)
        args += " --no-build";
      if (config.NoDeps)
        args += " --no-deps";
      if (config.NoStart)
        args += " --no-start";
      if (config.Wait)
        args += " --wait";
      if (config.WaitTimeout.HasValue)
        args += $" --wait-timeout {config.WaitTimeout.Value}";
      if (!string.IsNullOrEmpty(config.Pull))
        args += $" --pull {config.Pull}";
      if (config.Scale != null && config.Scale.Count > 0)
        foreach (var scale in config.Scale)
          args += $" --scale {scale.Key}={scale.Value}";
      if (config.Timeout.HasValue)
        args += $" --timeout {config.Timeout.Value}";
      return args;
    }

    /// <summary>
    /// Builds args for <c>docker compose down</c>.
    /// </summary>
    public static string BuildDownSubArgs(ComposeDownConfig config)
    {
      var args = "down";
      if (config.RemoveVolumes)
        args += " --volumes";
      if (!string.IsNullOrEmpty(config.RemoveImages))
        args += $" --rmi {config.RemoveImages}";
      if (config.RemoveOrphans)
        args += " --remove-orphans";
      if (config.Timeout.HasValue)
        args += $" --timeout {config.Timeout.Value}";
      return args;
    }

    /// <summary>
    /// Builds args for <c>docker compose restart</c> (flags only, no services).
    /// </summary>
    public static string BuildRestartSubArgs(ComposeRestartConfig config)
    {
      var args = "restart";
      if (config.Timeout.HasValue)
        args += $" --timeout {config.Timeout.Value}";
      if (config.NoDeps)
        args += " --no-deps";
      return args;
    }

    /// <summary>
    /// Builds args for <c>docker compose logs</c> (flags only, no services).
    /// </summary>
    public static string BuildLogsSubArgs(ComposeLogsConfig config)
    {
      var args = "logs";
      if (config.Follow)
        args += " -f";
      if (config.Timestamps)
        args += " -t";
      if (config.Tail.HasValue)
        args += $" --tail {config.Tail.Value}";
      if (!string.IsNullOrEmpty(config.Since))
        args += $" --since {QuoteIfNeeded(config.Since)}";
      if (!string.IsNullOrEmpty(config.Until))
        args += $" --until {QuoteIfNeeded(config.Until)}";
      if (config.NoColor)
        args += " --no-color";
      return args;
    }

    /// <summary>
    /// Builds args for <c>docker compose config</c>.
    /// </summary>
    public static string BuildConfigSubArgs(ComposeConfigConfig config)
    {
      var args = "config";
      if (config.ShowServices)
        args += " --services";
      if (config.ShowVolumes)
        args += " --volumes";
      if (config.ResolveImageDigests)
        args += " --resolve-image-digests";
      if (!string.IsNullOrEmpty(config.Format))
        args += $" --format {config.Format}";
      return args;
    }

    /// <summary>
    /// Builds args for <c>docker compose build</c> (flags only, no services).
    /// </summary>
    public static string BuildBuildSubArgs(ComposeBuildConfig config)
    {
      var args = "build";
      if (config.NoCache)
        args += " --no-cache";
      if (config.Pull)
        args += " --pull";
      if (config.ForceRm)
        args += " --force-rm";
      if (config.Parallel)
        args += " --parallel";
      if (config.BuildArgs != null)
        foreach (var ba in config.BuildArgs)
          args += $" --build-arg {ba.Key}={ba.Value}";
      return args;
    }

    /// <summary>
    /// Builds args for <c>docker compose pull</c> (flags only, no services).
    /// </summary>
    public static string BuildPullSubArgs(ComposePullConfig config)
    {
      var args = "pull";
      if (config.Quiet)
        args += " -q";
      if (config.IgnorePullFailures)
        args += " --ignore-pull-failures";
      if (config.IncludeDeps)
        args += " --include-deps";
      return args;
    }

    /// <summary>
    /// Builds args for <c>docker compose run</c> (includes service and command).
    /// </summary>
    public static string BuildRunSubArgs(ComposeRunConfig config)
    {
      var args = "run";
      if (config.Detach)
        args += " -d";
      if (config.Rm)
        args += " --rm";
      if (config.NoDeps)
        args += " --no-deps";
      if (!string.IsNullOrEmpty(config.Name))
        args += $" --name {QuoteIfNeeded(config.Name)}";
      if (!string.IsNullOrEmpty(config.User))
        args += $" -u {QuoteIfNeeded(config.User)}";
      if (!string.IsNullOrEmpty(config.Entrypoint))
        args += $" --entrypoint {QuoteIfNeeded(config.Entrypoint)}";
      if (!string.IsNullOrEmpty(config.WorkDir))
        args += $" -w {QuoteIfNeeded(config.WorkDir)}";
      if (config.ServicePorts)
        args += " --service-ports";
      if (config.Publish != null)
        foreach (var p in config.Publish)
          args += $" -p {p}";
      if (config.Volumes != null)
        foreach (var v in config.Volumes)
          args += $" -v {v}";
      if (!config.Tty)
        args += " -T";
      args += $" {config.Service}";
      if (config.Command != null && config.Command.Length > 0)
        args += " " + string.Join(" ", config.Command);
      return args;
    }

    /// <summary>
    /// Builds args for <c>docker compose up -d --scale</c> (used by ScaleAsync).
    /// </summary>
    public static string BuildScaleSubArgs(ComposeScaleConfig config)
    {
      var args = "up -d";
      if (config.NoDeps)
        args += " --no-deps";
      foreach (var scale in config.Scale)
        args += $" --scale {scale.Key}={scale.Value}";
      return args;
    }

    /// <summary>
    /// Builds args for <c>docker compose create</c> (flags only, no services).
    /// </summary>
    public static string BuildCreateSubArgs(ComposeCreateConfig config)
    {
      var args = "create";
      if (config.Build)
        args += " --build";
      if (config.ForceRecreate)
        args += " --force-recreate";
      if (config.NoRecreate)
        args += " --no-recreate";
      if (config.NoBuild)
        args += " --no-build";
      if (!string.IsNullOrEmpty(config.Pull))
        args += $" --pull {config.Pull}";
      if (config.RemoveOrphans)
        args += " --remove-orphans";
      return args;
    }

    #endregion

    #region Parsing

    /// <summary>
    /// Parses <c>docker compose ps --format json</c> output.
    /// Handles both JSON array format (Docker Compose v2.21+) and
    /// newline-delimited JSON (older versions).
    /// </summary>
    public static IList<ComposeServiceInfo> ParseServiceList(string json)
    {
      var services = new List<ComposeServiceInfo>();
      if (string.IsNullOrWhiteSpace(json))
        return services;

      var trimmed = json.Trim();
      if (trimmed.StartsWith('['))
      {
        var list = JsonSerializer.Deserialize<List<ComposeServiceInfo>>(trimmed, JsonHelper.CaseInsensitiveOptions);
        if (list != null)
          services.AddRange(list);
      }
      else
      {
        var lines = trimmed.Split(
            LineSeparators, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
          try
          {
            var service = JsonSerializer.Deserialize<ComposeServiceInfo>(line, JsonHelper.CaseInsensitiveOptions);
            if (service != null)
              services.Add(service);
          }
          catch (Exception ex) { Logger.Log($"Compose service info JSON parsing failed: {ex.Message}"); }
        }
      }

      return services;
    }

    #endregion

    #region Private Helpers (static)

    private static string QuoteIfNeeded(string argument)
    {
      if (string.IsNullOrEmpty(argument))
        return argument;
      if (!argument.Contains(' ') && !argument.Contains('\t'))
        return argument;
      var escaped = argument.Replace("\\", "\\\\").Replace("\"", "\\\"");
      return $"\"{escaped}\"";
    }

    #endregion
  }
}
