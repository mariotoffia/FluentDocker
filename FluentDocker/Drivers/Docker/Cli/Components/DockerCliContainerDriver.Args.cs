#nullable disable warnings
using System;
using System.Collections.Generic;
using System.Linq;
using FluentDocker.Drivers;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Drivers.Docker.Cli.Components
{
  public partial class DockerCliContainerDriver
  {
    private static List<string> BuildCreateArgs(
        string command, ContainerCreateConfig config, bool detach = false, string cidFile = null)
    {
      var args = new List<string> { command };
      if (detach)
        args.Add("-d");
      if (!string.IsNullOrEmpty(cidFile))
        args.Add($"--cidfile {QuoteArgumentIfNeeded(cidFile)}");
      if (!string.IsNullOrEmpty(config.Name))
        args.Add($"--name {QuotePositionalArgument(config.Name, nameof(config.Name))}");
      if (!string.IsNullOrEmpty(config.Hostname))
        args.Add($"--hostname {QuoteArgumentIfNeeded(config.Hostname)}");
      if (!string.IsNullOrEmpty(config.User))
        args.Add($"-u {QuoteArgumentIfNeeded(config.User)}");
      if (!string.IsNullOrEmpty(config.WorkingDirectory))
        args.Add($"-w {QuoteArgumentIfNeeded(config.WorkingDirectory)}");
      if (!string.IsNullOrEmpty(config.NetworkMode))
        args.Add($"--network {QuoteArgumentIfNeeded(config.NetworkMode)}");
      if (!string.IsNullOrEmpty(config.RestartPolicy))
        args.Add($"--restart {QuoteArgumentIfNeeded(config.RestartPolicy)}");
      if (!string.IsNullOrEmpty(config.StopSignal))
        args.Add($"--stop-signal {QuoteArgumentIfNeeded(config.StopSignal)}");
      if (config.StopTimeout.HasValue)
        args.Add($"--stop-timeout {FormatInvariant(config.StopTimeout.Value)}");
      if (config.Privileged)
        args.Add("--privileged");
      if (config.AutoRemove)
        args.Add("--rm");
      if (config.Tty)
        args.Add("-t");
      if (config.Interactive)
        args.Add("-i");
      if (config.MemoryLimit.HasValue && config.MemoryLimit.Value > 0)
        args.Add($"--memory {FormatInvariant(config.MemoryLimit.Value)}");
      if (config.CpuShares.HasValue && config.CpuShares.Value > 0)
        args.Add($"--cpu-shares {FormatInvariant(config.CpuShares.Value)}");
      if (config.CpuQuota.HasValue && config.CpuQuota.Value > 0)
        args.Add($"--cpu-quota {FormatInvariant(config.CpuQuota.Value)}");
      if (!string.IsNullOrEmpty(config.Ipv4Address))
        args.Add($"--ip {QuoteArgumentIfNeeded(config.Ipv4Address)}");
      if (!string.IsNullOrEmpty(config.Ipv6Address))
        args.Add($"--ip6 {QuoteArgumentIfNeeded(config.Ipv6Address)}");
      if (config.ReadonlyRootfs)
        args.Add("--read-only");
      if (config.ShmSize.HasValue && config.ShmSize.Value > 0)
        args.Add($"--shm-size {FormatInvariant(config.ShmSize.Value)}");
      if (!string.IsNullOrEmpty(config.Platform))
        args.Add($"--platform {QuoteArgumentIfNeeded(config.Platform)}");
      if (!string.IsNullOrEmpty(config.Runtime))
        args.Add($"--runtime {QuoteArgumentIfNeeded(config.Runtime)}");

      AddRepeated(args, "--cap-add", config.CapAdd);
      AddRepeated(args, "--cap-drop", config.CapDrop);
      AddRepeated(args, "--security-opt", config.SecurityOpt);
      AddRepeated(args, "-e", config.Environment?.Select(e => $"{e.Key}={e.Value}"));
      AddRepeated(args, "-p", config.PortBindings?.Select(p => $"{p.Value}:{p.Key}"));
      AddRepeated(args, "-v", config.Volumes);
      AddRepeated(args, "--label", config.Labels?.Select(l => $"{l.Key}={l.Value}"));
      // Multiple --network flags require Docker 25+/API 1.44+ and user-defined networks.
      // NetworkMode already emitted a --network flag above; de-duplicate the union of
      // (NetworkMode + Networks) so the same network is never passed twice.
      AddRepeated(args, "--network", DistinctNetworks(config));
      AddRepeated(args, "--dns", config.Dns);
      AddRepeated(args, "--add-host", config.ExtraHosts?.Select(h => $"{h.Key}:{h.Value}"));
      AddRepeated(args, "--link", config.Links);

      if (config.NetworkAliases != null)
        foreach (var alias in config.NetworkAliases.SelectMany(a => a.Value))
          // ponytail: aliases are flattened; use network connect --alias if per-network aliasing matters.
          args.Add($"--network-alias {QuoteArgumentIfNeeded(alias)}");
      if (config.Tmpfs != null)
        foreach (var tmpfs in config.Tmpfs)
          args.Add(string.IsNullOrEmpty(tmpfs.Value)
              ? $"--tmpfs {QuoteArgumentIfNeeded(tmpfs.Key)}"
              : $"--tmpfs {QuoteArgumentIfNeeded($"{tmpfs.Key}:{tmpfs.Value}")}");
      if (config.Devices != null)
        foreach (var dev in config.Devices)
          args.Add(dev.Key == dev.Value
              ? $"--device {QuoteArgumentIfNeeded(dev.Key)}"
              : $"--device {QuoteArgumentIfNeeded($"{dev.Key}:{dev.Value}")}");

      AddHealthArgs(args, config.HealthCheck);

      string[] entrypointArgs = null;
      if (config.Entrypoint is { Length: > 0 })
      {
        args.Add($"--entrypoint {QuoteArgumentIfNeeded(config.Entrypoint[0])}");
        if (config.Entrypoint.Length > 1)
          entrypointArgs = config.Entrypoint[1..];
      }

      args.Add(QuotePositionalArgument(config.Image, nameof(config.Image)));
      if (entrypointArgs != null)
        args.AddRange(entrypointArgs.Select(QuoteArgumentIfNeeded));
      if (config.Command is { Length: > 0 })
        args.AddRange(config.Command.Select(QuoteArgumentIfNeeded));
      return args;
    }

    // Order-preserving union: entries equal to NetworkMode (already emitted) or repeated
    // within Networks are dropped so each network yields exactly one --network flag.
    private static IEnumerable<string> DistinctNetworks(ContainerCreateConfig config)
    {
      if (config.Networks == null)
        yield break;

      var seen = new HashSet<string>(StringComparer.Ordinal);
      if (!string.IsNullOrEmpty(config.NetworkMode))
        seen.Add(config.NetworkMode);
      foreach (var network in config.Networks)
        if (seen.Add(network))
          yield return network;
    }

    private static void AddRepeated(List<string> args, string flag, IEnumerable<string> values)
    {
      if (values == null)
        return;
      foreach (var value in values)
        args.Add($"{flag} {QuoteArgumentIfNeeded(value)}");
    }

    private static void AddHealthArgs(List<string> args, HealthCheckConfig healthCheck)
    {
      if (healthCheck == null)
        return;
      if (healthCheck.Test is { Length: 1 } &&
          string.Equals(healthCheck.Test[0], "NONE", StringComparison.OrdinalIgnoreCase))
      {
        args.Add("--no-healthcheck");
        return;
      }
      else if (healthCheck.Test is { Length: > 0 })
      {
        // ponytail: doc-only Windows health-cmd caveat; add OS-aware translation when config exposes container OS.
        var test = healthCheck.Test;
        var command = test[0] == "CMD-SHELL"
            ? string.Join(" ", test[1..])
            : string.Join(" ", (test[0] == "CMD" ? test[1..] : test).Select(ShellQuoteHealthToken));
        args.Add($"--health-cmd {QuoteArgumentIfNeeded(command)}");
      }
      if (!string.IsNullOrEmpty(healthCheck.Interval))
        args.Add($"--health-interval {QuoteArgumentIfNeeded(healthCheck.Interval)}");
      if (!string.IsNullOrEmpty(healthCheck.Timeout))
        args.Add($"--health-timeout {QuoteArgumentIfNeeded(healthCheck.Timeout)}");
      if (healthCheck.Retries > 0)
        args.Add($"--health-retries {FormatInvariant(healthCheck.Retries)}");
      if (!string.IsNullOrEmpty(healthCheck.StartPeriod))
        args.Add($"--health-start-period {QuoteArgumentIfNeeded(healthCheck.StartPeriod)}");
    }

    private static string ShellQuoteHealthToken(string value)
    {
      if (string.IsNullOrEmpty(value))
        return "''";
      return value.Any(c => char.IsWhiteSpace(c) || "|&;()<>$`'\"\\*?[]{}!#~=".Contains(c))
          ? $"'{value.Replace("'", "'\"'\"'")}'"
          : value;
    }
  }
}
