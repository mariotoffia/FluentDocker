using System;
using System.Collections.Generic;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Cli;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Docker
{
  /// <summary>
  /// Unit tests for DockerCliContainerDriver operation arg building:
  /// exec, update, RunAsync advanced features (security, health, entrypoint).
  /// </summary>
  [Trait("Category", "Unit")]
  public partial class DockerCliContainerDriverTests
  {
    #region Exec Arg Building Tests

    [Fact]
    public void ExecArgs_MinimalCommand_StartsWithExecAndContainerId()
    {
      var args = BuildExecArgs("ctr123", new ExecConfig
      { Command = ["ls", "-la"] });
      Assert.StartsWith("exec", args);
      Assert.Contains("ctr123", args);
      Assert.EndsWith("ls -la", args);
    }

    [Fact]
    public void ExecArgs_WithDetach_IncludesDetachFlag()
    {
      var args = BuildExecArgs("ctr123", new ExecConfig
      { Detach = true, Command = ["bash"] });
      Assert.Contains("-d", args);
    }

    [Fact]
    public void ExecArgs_WithInteractive_IncludesInteractiveFlag()
    {
      var args = BuildExecArgs("ctr123", new ExecConfig
      { Interactive = true, Command = ["bash"] });
      Assert.Contains("-i", args);
    }

    [Fact]
    public void ExecArgs_WithTty_IncludesTtyFlag()
    {
      var args = BuildExecArgs("ctr123", new ExecConfig
      { Tty = true, Command = ["bash"] });
      Assert.Contains("-t", args);
    }

    [Fact]
    public void ExecArgs_WithPrivileged_IncludesPrivilegedFlag()
    {
      var args = BuildExecArgs("ctr123", new ExecConfig
      { Privileged = true, Command = ["bash"] });
      Assert.Contains("--privileged", args);
    }

    [Fact]
    public void ExecArgs_WithUser_IncludesUserFlag()
    {
      var args = BuildExecArgs("ctr123", new ExecConfig
      { User = "root", Command = ["bash"] });
      Assert.Contains("-u root", args);
    }

    [Fact]
    public void ExecArgs_WithWorkDir_IncludesWorkDirFlag()
    {
      var args = BuildExecArgs("ctr123", new ExecConfig
      { WorkingDir = "/app", Command = ["ls"] });
      Assert.Contains("-w /app", args);
    }

    [Fact]
    public void ExecArgs_WithEnvironment_IncludesEnvFlags()
    {
      var args = BuildExecArgs("ctr123", new ExecConfig
      {
        Environment = new Dictionary<string, string>
          { { "FOO", "bar" }, { "BAZ", "qux" } },
        Command = ["env"]
      });
      Assert.Contains("-e FOO=bar", args);
      Assert.Contains("-e BAZ=qux", args);
    }

    [Fact]
    public void ExecArgs_AllFlags_FlagsBeforeContainerId()
    {
      var args = BuildExecArgs("ctr123", new ExecConfig
      {
        Detach = true,
        Interactive = true,
        Tty = true,
        Privileged = true,
        User = "root",
        WorkingDir = "/app",
        Environment = new Dictionary<string, string> { { "K", "V" } },
        Command = ["bash"]
      });
      var ctrIdx = args.IndexOf("ctr123", StringComparison.Ordinal);
      Assert.True(args.IndexOf("-d", StringComparison.Ordinal) < ctrIdx);
      Assert.True(args.IndexOf("--privileged", StringComparison.Ordinal) < ctrIdx);
      Assert.True(args.IndexOf("bash", StringComparison.Ordinal) > ctrIdx);
    }

    [Fact]
    public void ExecArgs_CommandWithSpaces_QuotesCorrectly()
    {
      var args = BuildExecArgs("ctr123", new ExecConfig
      { Command = ["sh", "-c", "echo hello world"] });
      Assert.Contains("\"echo hello world\"", args);
    }

    #endregion

    #region Update Arg Building Tests

    [Fact]
    public void UpdateArgs_WithMemoryLimit_IncludesMemoryFlag()
    {
      var args = BuildUpdateArgs("ctr123",
          new ContainerUpdateConfig { MemoryLimit = 536870912 });
      Assert.StartsWith("update", args);
      Assert.Contains("--memory 536870912", args);
      Assert.EndsWith("ctr123", args);
    }

    [Fact]
    public void UpdateArgs_WithMemorySwap_IncludesMemorySwapFlag()
    {
      var args = BuildUpdateArgs("ctr123",
          new ContainerUpdateConfig { MemorySwap = -1 });
      Assert.Contains("--memory-swap -1", args);
    }

    [Fact]
    public void UpdateArgs_WithMemoryReservation_IncludesFlag()
    {
      var args = BuildUpdateArgs("ctr123",
          new ContainerUpdateConfig { MemoryReservation = 268435456 });
      Assert.Contains("--memory-reservation 268435456", args);
    }

    [Fact]
    public void UpdateArgs_WithCpuShares_IncludesCpuSharesFlag()
    {
      var args = BuildUpdateArgs("ctr123",
          new ContainerUpdateConfig { CpuShares = 512 });
      Assert.Contains("--cpu-shares 512", args);
    }

    [Fact]
    public void UpdateArgs_WithCpuPeriod_IncludesCpuPeriodFlag()
    {
      var args = BuildUpdateArgs("ctr123",
          new ContainerUpdateConfig { CpuPeriod = 100000 });
      Assert.Contains("--cpu-period 100000", args);
    }

    [Fact]
    public void UpdateArgs_WithCpuQuota_IncludesCpuQuotaFlag()
    {
      var args = BuildUpdateArgs("ctr123",
          new ContainerUpdateConfig { CpuQuota = 50000 });
      Assert.Contains("--cpu-quota 50000", args);
    }

    [Fact]
    public void UpdateArgs_WithCpusetCpus_IncludesCpusetFlag()
    {
      var args = BuildUpdateArgs("ctr123",
          new ContainerUpdateConfig { CpusetCpus = "0-3" });
      Assert.Contains("--cpuset-cpus 0-3", args);
    }

    [Fact]
    public void UpdateArgs_WithRestartPolicy_IncludesRestartFlag()
    {
      var args = BuildUpdateArgs("ctr123",
          new ContainerUpdateConfig { RestartPolicy = "always" });
      Assert.Contains("--restart always", args);
    }

    [Fact]
    public void UpdateArgs_WithPidsLimit_IncludesPidsLimitFlag()
    {
      var args = BuildUpdateArgs("ctr123",
          new ContainerUpdateConfig { PidsLimit = 100 });
      Assert.Contains("--pids-limit 100", args);
    }

    [Fact]
    public void UpdateArgs_AllFields_ContainerIdIsLast()
    {
      var args = BuildUpdateArgs("ctr123", new ContainerUpdateConfig
      { MemoryLimit = 100, CpuShares = 512, RestartPolicy = "always" });
      Assert.EndsWith("ctr123", args);
    }

    [Fact]
    public void UpdateArgs_NoFields_OnlyUpdateAndContainerId()
    {
      var args = BuildUpdateArgs("ctr123", new ContainerUpdateConfig());
      Assert.Equal("update ctr123", args);
    }

    #endregion

    #region RunAsync Advanced Feature Tests

    [Fact]
    public void RunArgs_Detached_IncludesDetachFlag()
    {
      var args = BuildRunArgs(new ContainerCreateConfig
      { Image = "nginx", Detach = true });
      Assert.Contains("-d", args);
    }

    [Fact]
    public void RunArgs_WithCapabilities_IncludesCapFlags()
    {
      var args = BuildRunArgs(new ContainerCreateConfig
      {
        Image = "nginx",
        Detach = true,
        CapAdd = ["SYS_PTRACE"],
        CapDrop = ["NET_RAW"]
      });
      Assert.Contains("--cap-add SYS_PTRACE", args);
      Assert.Contains("--cap-drop NET_RAW", args);
    }

    [Fact]
    public void RunArgs_WithNetworkAliases_IncludesAliasFlags()
    {
      var args = BuildRunArgs(new ContainerCreateConfig
      {
        Image = "nginx",
        Detach = true,
        NetworkAliases = new Dictionary<string, List<string>>
          { { "mynet", new List<string> { "web", "proxy" } } }
      });
      Assert.Contains("--network-alias web", args);
      Assert.Contains("--network-alias proxy", args);
    }

    [Fact]
    public void RunArgs_WithHealthCheck_StripsCommandPrefix()
    {
      var args = BuildRunArgs(new ContainerCreateConfig
      {
        Image = "nginx",
        Detach = true,
        HealthCheck = new HealthCheckConfig
        {
          Test = ["CMD-SHELL", "curl -f http://localhost/"],
          Interval = "30s",
          Timeout = "10s",
          Retries = 3
        }
      });
      Assert.Contains("--health-cmd", args);
      Assert.Contains("--health-interval 30s", args);
      Assert.DoesNotContain("CMD-SHELL", args);
    }

    [Fact]
    public void RunArgs_WithEntrypointOverflow_ArgsBeforeCommand()
    {
      var args = BuildRunArgs(new ContainerCreateConfig
      {
        Image = "ubuntu",
        Detach = true,
        Entrypoint = ["/bin/sh", "-c"],
        Command = ["echo", "test"]
      });
      Assert.Contains("--entrypoint /bin/sh", args);
      var imgIdx = args.IndexOf("ubuntu", StringComparison.Ordinal);
      var dashCIdx = args.IndexOf("-c", imgIdx, StringComparison.Ordinal);
      var echoIdx = args.IndexOf("echo", StringComparison.Ordinal);
      Assert.True(dashCIdx > imgIdx);
      Assert.True(echoIdx > dashCIdx);
    }

    [Fact]
    public void RunArgs_WithDnsAndExtraHosts_IncludesFlags()
    {
      var args = BuildRunArgs(new ContainerCreateConfig
      {
        Image = "nginx",
        Detach = true,
        Dns = ["8.8.8.8"],
        ExtraHosts = new Dictionary<string, string>
          { { "host1", "192.168.1.1" } }
      });
      Assert.Contains("--dns 8.8.8.8", args);
      Assert.Contains("--add-host host1:192.168.1.1", args);
    }

    [Fact]
    public void RunArgs_WithTmpfs_IncludesTmpfsFlags()
    {
      var args = BuildRunArgs(new ContainerCreateConfig
      {
        Image = "nginx",
        Detach = true,
        Tmpfs = new Dictionary<string, string>
          { { "/tmp", "rw,noexec" }, { "/run", "" } }
      });
      Assert.Contains("--tmpfs /tmp:rw,noexec", args);
      Assert.Contains("--tmpfs /run", args);
    }

    [Fact]
    public void RunArgs_WithDevices_SamePathNoColon()
    {
      var args = BuildRunArgs(new ContainerCreateConfig
      {
        Image = "nvidia",
        Detach = true,
        Devices = new Dictionary<string, string>
        {
          { "/dev/sda", "/dev/xvdc" },
          { "/dev/null", "/dev/null" }
        }
      });
      Assert.Contains("--device /dev/sda:/dev/xvdc", args);
      Assert.Contains("--device /dev/null", args);
      // Same device should not have colon format
      Assert.DoesNotContain("--device /dev/null:/dev/null", args);
    }

    [Fact]
    public void RunArgs_WithStopSignalAndTimeout_IncludesFlags()
    {
      var args = BuildRunArgs(new ContainerCreateConfig
      {
        Image = "nginx",
        Detach = true,
        StopSignal = "SIGTERM",
        StopTimeout = 30
      });
      Assert.Contains("--stop-signal SIGTERM", args);
      Assert.Contains("--stop-timeout 30", args);
    }

    #endregion

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
          args.Add($"-v {Quote($"{v.Key}:{v.Value}")}");
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
      string[] epArgs = null;
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
