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


  }
}
