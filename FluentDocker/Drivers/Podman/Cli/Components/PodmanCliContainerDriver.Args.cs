using System.Collections.Generic;
using System.Linq;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Drivers.Podman.Cli.Components
{
  /// <summary>
  /// Podman CLI container driver — CLI argument building. Split into its own partial file
  /// purely to keep each source file within the repository's 500-line limit.
  /// </summary>
  public partial class PodmanCliContainerDriver
  {
    #region Argument Building

    /// <summary>
    /// Builds the CLI arguments string for <c>podman ps</c>.
    /// </summary>
    public static string BuildListArgs(ContainerListFilter filter)
    {
      var args = "ps --format json";
      if (filter == null)
        return args;

      if (filter.All)
        args += " -a";
      if (!string.IsNullOrEmpty(filter.Name))
        args += $" --filter {QuoteArgumentIfNeeded($"name={filter.Name}")}";
      if (!string.IsNullOrEmpty(filter.Status))
        args += $" --filter {QuoteArgumentIfNeeded($"status={filter.Status}")}";
      if (!string.IsNullOrEmpty(filter.Id))
        args += $" --filter {QuoteArgumentIfNeeded($"id={filter.Id}")}";
      if (!string.IsNullOrEmpty(filter.Ancestor))
        args += $" --filter {QuoteArgumentIfNeeded($"ancestor={filter.Ancestor}")}";
      if (filter.Labels != null)
      {
        foreach (var label in filter.Labels)
          args += string.IsNullOrEmpty(label.Value)
              ? $" --filter {QuoteArgumentIfNeeded($"label={label.Key}")}"
              : $" --filter {QuoteArgumentIfNeeded($"label={label.Key}={label.Value}")}";
      }
      if (filter.Limit.HasValue)
        args += $" --last {filter.Limit.Value}";

      return args;
    }

    private static string BuildCreateArgs(string command, ContainerCreateConfig config, bool detach = false)
    {
      var args = detach ? $"{command} -d" : command;

      if (!string.IsNullOrEmpty(config.Name))
        args += $" --name {QuoteArgumentIfNeeded(config.Name)}";
      if (!string.IsNullOrEmpty(config.Hostname))
        args += $" --hostname {QuoteArgumentIfNeeded(config.Hostname)}";
      if (!string.IsNullOrEmpty(config.User))
        args += $" --user {QuoteArgumentIfNeeded(config.User)}";
      if (!string.IsNullOrEmpty(config.WorkingDirectory))
        args += $" -w {QuoteArgumentIfNeeded(config.WorkingDirectory)}";
      if (!string.IsNullOrEmpty(config.NetworkMode))
        args += $" --network {QuoteArgumentIfNeeded(config.NetworkMode)}";
      if (!string.IsNullOrEmpty(config.RestartPolicy))
        args += $" --restart {QuoteArgumentIfNeeded(config.RestartPolicy)}";
      if (!string.IsNullOrEmpty(config.StopSignal))
        args += $" --stop-signal {QuoteArgumentIfNeeded(config.StopSignal)}";
      if (config.StopTimeout.HasValue)
        args += $" --stop-timeout {config.StopTimeout.Value}";
      if (config.Privileged)
        args += " --privileged";
      if (config.AutoRemove)
        args += " --rm";
      if (config.Tty)
        args += " -t";
      if (config.Interactive)
        args += " -i";
      if (config.MemoryLimit.HasValue)
        args += $" --memory {config.MemoryLimit.Value}";
      if (config.CpuShares.HasValue)
        args += $" --cpu-shares {config.CpuShares.Value}";
      if (config.CpuQuota.HasValue)
        args += $" --cpu-quota {config.CpuQuota.Value}";
      if (!string.IsNullOrEmpty(config.Ipv4Address))
        args += $" --ip {QuoteArgumentIfNeeded(config.Ipv4Address)}";
      if (!string.IsNullOrEmpty(config.Ipv6Address))
        args += $" --ip6 {QuoteArgumentIfNeeded(config.Ipv6Address)}";
      if (!string.IsNullOrEmpty(config.Pod))
        args += $" --pod {QuoteArgumentIfNeeded(config.Pod)}";
      if (config.ReadonlyRootfs)
        args += " --read-only";
      if (config.ShmSize.HasValue)
        args += $" --shm-size {config.ShmSize.Value}";
      if (!string.IsNullOrEmpty(config.Platform))
        args += $" --platform {QuoteArgumentIfNeeded(config.Platform)}";
      if (!string.IsNullOrEmpty(config.Runtime))
        args += $" --runtime {QuoteArgumentIfNeeded(config.Runtime)}";

      foreach (var cap in OrEmpty(config.CapAdd))
        args += $" --cap-add {QuoteArgumentIfNeeded(cap)}";
      foreach (var cap in OrEmpty(config.CapDrop))
        args += $" --cap-drop {QuoteArgumentIfNeeded(cap)}";
      foreach (var opt in OrEmpty(config.SecurityOpt))
        args += $" --security-opt {QuoteArgumentIfNeeded(opt)}";
      foreach (var tmpfs in OrEmpty(config.Tmpfs))
        args += string.IsNullOrEmpty(tmpfs.Value)
            ? $" --tmpfs {QuoteArgumentIfNeeded(tmpfs.Key)}" : $" --tmpfs {QuoteArgumentIfNeeded($"{tmpfs.Key}:{tmpfs.Value}")}";
      foreach (var dev in OrEmpty(config.Devices))
        args += dev.Key == dev.Value
            ? $" --device {QuoteArgumentIfNeeded(dev.Key)}" : $" --device {QuoteArgumentIfNeeded($"{dev.Key}:{dev.Value}")}";
      foreach (var env in OrEmpty(config.Environment))
        args += $" -e {QuoteArgumentIfNeeded($"{env.Key}={env.Value}")}";
      foreach (var port in OrEmpty(config.PortBindings))
        args += $" -p {QuoteArgumentIfNeeded($"{port.Value}:{port.Key}")}";
      foreach (var vol in OrEmpty(config.Volumes))
        args += $" -v {QuoteArgumentIfNeeded(vol)}";
      foreach (var label in OrEmpty(config.Labels))
        args += $" --label {QuoteArgumentIfNeeded($"{label.Key}={label.Value}")}";
      foreach (var network in OrEmpty(config.Networks))
        args += $" --network {QuoteArgumentIfNeeded(network)}";
      foreach (var dns in OrEmpty(config.Dns))
        args += $" --dns {QuoteArgumentIfNeeded(dns)}";
      foreach (var host in OrEmpty(config.ExtraHosts))
        args += $" --add-host {QuoteArgumentIfNeeded($"{host.Key}:{host.Value}")}";
      foreach (var link in OrEmpty(config.Links))
        args += $" --link {QuoteArgumentIfNeeded(link)}";
      foreach (var networkAlias in OrEmpty(config.NetworkAliases))
        foreach (var alias in OrEmpty(networkAlias.Value))
          args += $" --network-alias {QuoteArgumentIfNeeded(alias)}";

      // Entrypoint — Podman CLI --entrypoint only accepts the executable.
      // Additional arguments from the entrypoint array are prepended to Command below.
      string[] entrypointArgs = null;
      if (config.Entrypoint != null && config.Entrypoint.Length > 0)
      {
        args += $" --entrypoint {QuoteArgumentIfNeeded(config.Entrypoint[0])}";
        if (config.Entrypoint.Length > 1)
          entrypointArgs = config.Entrypoint[1..];
      }

      if (config.HealthCheck != null)
      {
        if (config.HealthCheck.Test != null && config.HealthCheck.Test.Length > 0)
          args += $" --health-cmd {QuoteArgumentIfNeeded(string.Join(" ", config.HealthCheck.Test))}";
        if (!string.IsNullOrEmpty(config.HealthCheck.Interval))
          args += $" --health-interval {QuoteArgumentIfNeeded(config.HealthCheck.Interval)}";
        if (!string.IsNullOrEmpty(config.HealthCheck.Timeout))
          args += $" --health-timeout {QuoteArgumentIfNeeded(config.HealthCheck.Timeout)}";
        if (config.HealthCheck.Retries > 0)
          args += $" --health-retries {config.HealthCheck.Retries}";
        if (!string.IsNullOrEmpty(config.HealthCheck.StartPeriod))
          args += $" --health-start-period {QuoteArgumentIfNeeded(config.HealthCheck.StartPeriod)}";
      }

      args += $" {QuotePositionalArgument(config.Image, nameof(config.Image))}";

      // Entrypoint overflow args come before Command
      if (entrypointArgs != null)
        args += " " + string.Join(" ", entrypointArgs.Select(QuoteArgumentIfNeeded));

      if (config.Command != null && config.Command.Length > 0)
        args += " " + string.Join(" ", config.Command.Select(QuoteArgumentIfNeeded));

      return args;
    }

    /// <summary>
    /// Null-safe enumeration source. The fluent <c>ContainerBuilder</c> nulls out empty
    /// collections before calling the driver (<c>ContainerBuilder.ExecuteAsync</c>), so every
    /// collection walked by <see cref="BuildCreateArgs"/> must tolerate a null. Routing all
    /// loops through this one helper fixes the NRE once for every argument the builder can emit.
    /// </summary>
    private static IEnumerable<T> OrEmpty<T>(IEnumerable<T> source) => source ?? Enumerable.Empty<T>();

    #endregion
  }
}
