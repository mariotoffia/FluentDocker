using System.Collections.Generic;
using FluentDocker.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Docker
{
  [Trait("Category", "Unit")]
  public partial class DockerCliContainerDriverTests
  {
    #region Operation Arg-Building Helpers

    private static string BuildExecArgs(string containerId, ExecConfig config)
    {
      var args = new List<string> { "exec" };
      if (config.Detach)
        args.Add("-d");
      if (config.Interactive)
        args.Add("-i");
      if (config.Tty)
        args.Add("-t");
      if (config.Privileged)
        args.Add("--privileged");
      if (!string.IsNullOrEmpty(config.User))
        args.Add($"-u {Quote(config.User)}");
      if (!string.IsNullOrEmpty(config.WorkingDir))
        args.Add($"-w {Quote(config.WorkingDir)}");
      if (config.Environment != null)
        foreach (var env in config.Environment)
          args.Add($"-e {Quote($"{env.Key}={env.Value}")}");
      args.Add(Quote(containerId));
      if (config.Command != null)
        foreach (var c in config.Command)
          args.Add(Quote(c));
      return string.Join(" ", args);
    }

    private static string BuildUpdateArgs(
        string containerId, ContainerUpdateConfig config)
    {
      var args = new List<string> { "update" };
      if (config.MemoryLimit.HasValue)
        args.Add($"--memory {config.MemoryLimit.Value}");
      if (config.MemorySwap.HasValue)
        args.Add($"--memory-swap {config.MemorySwap.Value}");
      if (config.MemoryReservation.HasValue)
        args.Add($"--memory-reservation {config.MemoryReservation.Value}");
      if (config.CpuShares.HasValue)
        args.Add($"--cpu-shares {config.CpuShares.Value}");
      if (config.CpuPeriod.HasValue)
        args.Add($"--cpu-period {config.CpuPeriod.Value}");
      if (config.CpuQuota.HasValue)
        args.Add($"--cpu-quota {config.CpuQuota.Value}");
      if (!string.IsNullOrEmpty(config.CpusetCpus))
        args.Add($"--cpuset-cpus {Quote(config.CpusetCpus)}");
      if (!string.IsNullOrEmpty(config.RestartPolicy))
        args.Add($"--restart {Quote(config.RestartPolicy)}");
      if (config.PidsLimit.HasValue)
        args.Add($"--pids-limit {config.PidsLimit.Value}");
      args.Add(Quote(containerId));
      return string.Join(" ", args);
    }

    private static string BuildRunArgs(ContainerCreateConfig config)
    {
      var args = new List<string> { "run" };
      if (config.Detach)
        args.Add("-d");
      if (!string.IsNullOrEmpty(config.Name))
        args.Add($"--name {Quote(config.Name)}");
      if (config.Environment != null)
        foreach (var e in config.Environment)
          args.Add($"-e {Quote($"{e.Key}={e.Value}")}");
      if (config.PortBindings != null)
        foreach (var p in config.PortBindings)
          args.Add($"-p {Quote($"{p.Value}:{p.Key}")}");
      if (config.Volumes != null)
        foreach (var v in config.Volumes)
          args.Add($"-v {Quote(v)}");
      if (!string.IsNullOrEmpty(config.NetworkMode))
        args.Add($"--network {Quote(config.NetworkMode)}");
      if (!string.IsNullOrEmpty(config.Ipv4Address))
        args.Add($"--ip {Quote(config.Ipv4Address)}");
      if (!string.IsNullOrEmpty(config.Ipv6Address))
        args.Add($"--ip6 {Quote(config.Ipv6Address)}");
      if (config.Labels != null)
        foreach (var l in config.Labels)
          args.Add($"--label {Quote($"{l.Key}={l.Value}")}");
      if (!string.IsNullOrEmpty(config.WorkingDirectory))
        args.Add($"-w {Quote(config.WorkingDirectory)}");
      if (!string.IsNullOrEmpty(config.User))
        args.Add($"-u {Quote(config.User)}");
      if (!string.IsNullOrEmpty(config.RestartPolicy))
        args.Add($"--restart {Quote(config.RestartPolicy)}");
      if (config.Privileged)
        args.Add("--privileged");
      if (config.AutoRemove)
        args.Add("--rm");
      if (config.Links != null)
        foreach (var lnk in config.Links)
          args.Add($"--link {Quote(lnk)}");
      if (config.NetworkAliases != null)
        foreach (var na in config.NetworkAliases)
          foreach (var a in na.Value)
            args.Add($"--network-alias {Quote(a)}");
      if (config.ReadonlyRootfs)
        args.Add("--read-only");
      if (config.ShmSize.HasValue)
        args.Add($"--shm-size {config.ShmSize.Value}");
      if (!string.IsNullOrEmpty(config.Platform))
        args.Add($"--platform {Quote(config.Platform)}");
      if (!string.IsNullOrEmpty(config.Runtime))
        args.Add($"--runtime {Quote(config.Runtime)}");
      if (config.CapAdd != null)
        foreach (var c in config.CapAdd)
          args.Add($"--cap-add {Quote(c)}");
      if (config.CapDrop != null)
        foreach (var c in config.CapDrop)
          args.Add($"--cap-drop {Quote(c)}");
      if (config.SecurityOpt != null)
        foreach (var o in config.SecurityOpt)
          args.Add($"--security-opt {Quote(o)}");
      if (config.Tmpfs != null)
        foreach (var t in config.Tmpfs)
          args.Add(string.IsNullOrEmpty(t.Value)
              ? $"--tmpfs {Quote(t.Key)}" : $"--tmpfs {Quote($"{t.Key}:{t.Value}")}");
      if (config.Devices != null)
        foreach (var d in config.Devices)
          args.Add(d.Key == d.Value
              ? $"--device {Quote(d.Key)}" : $"--device {Quote($"{d.Key}:{d.Value}")}");
      if (config.Tty)
        args.Add("-t");
      if (config.Interactive)
        args.Add("-i");
      if (config.HealthCheck != null)
      {
        if (config.HealthCheck.Test is { Length: > 0 })
        {
          var tc = config.HealthCheck.Test;
          if (tc[0] is "CMD-SHELL" or "CMD")
            tc = tc[1..];
          args.Add($"--health-cmd \"{string.Join(" ", tc)}\"");
        }
        if (!string.IsNullOrEmpty(config.HealthCheck.Interval))
          args.Add($"--health-interval {config.HealthCheck.Interval}");
        if (!string.IsNullOrEmpty(config.HealthCheck.Timeout))
          args.Add($"--health-timeout {config.HealthCheck.Timeout}");
        if (config.HealthCheck.Retries > 0)
          args.Add($"--health-retries {config.HealthCheck.Retries}");
        if (!string.IsNullOrEmpty(config.HealthCheck.StartPeriod))
          args.Add($"--health-start-period {config.HealthCheck.StartPeriod}");
      }
      if (config.MemoryLimit is > 0)
        args.Add($"--memory {config.MemoryLimit.Value}");
      if (config.CpuShares is > 0)
        args.Add($"--cpu-shares {config.CpuShares.Value}");
      if (!string.IsNullOrEmpty(config.Hostname))
        args.Add($"--hostname {Quote(config.Hostname)}");
      if (config.Dns != null)
        foreach (var dns in config.Dns)
          args.Add($"--dns {Quote(dns)}");
      if (config.ExtraHosts != null)
        foreach (var h in config.ExtraHosts)
          args.Add($"--add-host {Quote($"{h.Key}:{h.Value}")}");
      string[]? epArgs = null;
      if (config.Entrypoint is { Length: > 0 })
      {
        args.Add($"--entrypoint {Quote(config.Entrypoint[0])}");
        if (config.Entrypoint.Length > 1)
          epArgs = config.Entrypoint[1..];
      }
      if (!string.IsNullOrEmpty(config.StopSignal))
        args.Add($"--stop-signal {config.StopSignal}");
      if (config.StopTimeout.HasValue)
        args.Add($"--stop-timeout {config.StopTimeout.Value}");
      args.Add(config.Image);
      if (epArgs != null)
        foreach (var a in epArgs)
          args.Add(Quote(a));
      if (config.Command is { Length: > 0 })
        foreach (var c in config.Command)
          args.Add(Quote(c));
      return string.Join(" ", args);
    }

    #endregion

  }
}
