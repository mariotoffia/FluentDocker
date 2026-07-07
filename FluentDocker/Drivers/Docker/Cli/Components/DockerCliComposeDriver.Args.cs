using System;
using System.Collections.Generic;
using System.Linq;
using FluentDocker.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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
        args += $" --filter {QuoteIfNeeded($"status={config.Status}")}";
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
        args += $" --pull {QuoteIfNeeded(config.Pull)}";
      if (config.Scale != null && config.Scale.Count > 0)
        foreach (var scale in config.Scale)
          args += $" --scale {QuoteIfNeeded($"{scale.Key}={scale.Value}")}";
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
        args += $" --rmi {QuoteIfNeeded(config.RemoveImages)}";
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
        args += $" --format {QuoteIfNeeded(config.Format)}";
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
          args += $" --build-arg {QuoteIfNeeded($"{ba.Key}={ba.Value}")}";
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
          args += $" -p {QuoteIfNeeded(p)}";
      if (config.Volumes != null)
        foreach (var v in config.Volumes)
          args += $" -v {QuoteIfNeeded(v)}";
      if (!config.Tty)
        args += " -T";
      args += $" {QuotePositionalArgument(config.Service, nameof(config.Service))}";
      if (config.Command != null && config.Command.Length > 0)
        args += " " + string.Join(" ", config.Command.Select(QuoteIfNeeded));
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
        args += $" --scale {QuoteIfNeeded($"{scale.Key}={scale.Value}")}";
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
        args += $" --pull {QuoteIfNeeded(config.Pull)}";
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
    public static IList<ComposeServiceInfo> ParseServiceList(string json, ILogger logger = null)
    {
      return TryParseServiceList(json, logger, out var services, out _) ? services : services;
    }

    internal static bool TryParseServiceList(
        string json,
        ILogger logger,
        out IList<ComposeServiceInfo> services,
        out string error)
    {
      logger ??= NullLogger.Instance;
      services = new List<ComposeServiceInfo>();
      error = null;
      if (string.IsNullOrWhiteSpace(json))
        return true;

      var trimmed = json.Trim();
      if (trimmed.StartsWith('['))
      {
        if (!JsonHelper.TryDeserialize<List<ComposeServiceInfo>>(trimmed, out var list, out var parseError))
        {
          error = $"Compose service info JSON parsing failed: {parseError?.Message}";
          return false;
        }
        if (list != null)
          ((List<ComposeServiceInfo>)services).AddRange(list);
        return true;
      }

      var ok = DockerCliJsonLineParser.TryParse<ComposeServiceInfo>(
          trimmed,
          logger,
          "Compose service info JSON parsing failed",
          out var parsed,
          out error);
      services = parsed;
      return ok;
    }

    #endregion

    #region Private Helpers (static)

    // Delegates to the base class QuoteArgumentIfNeeded which checks
    // a broader set of shell metacharacters (;, &, |, $, `, etc.)
    // not just spaces and tabs.
    private static string QuoteIfNeeded(string argument) => QuoteArgumentIfNeeded(argument);

    #endregion
  }
}
