using System.Text;
using FluentDocker.Extensions;

namespace FluentDocker.Model.Containers
{
  public sealed partial class ContainerCreateParams
  {
    /// <summary>
    ///   Renders the argument string from this instance.
    /// </summary>
    public override string ToString()
    {
      var sb = new StringBuilder();

      // Container identification
      sb.OptionIfExists("--name ", Name);
      sb.OptionIfExists("--pid=", Pid);
      sb.OptionIfExists("--uts=", Uts);
      sb.OptionIfExists("--ipc=", Ipc);
      if (!string.IsNullOrWhiteSpace(CidFile))
        sb.Append($" --cidfile=\"{CidFile}\"");

      if (null != HostIpMappings && 0 != HostIpMappings.Count)
      {
        foreach (var mapping in HostIpMappings)
          sb.Append($" --add-host={mapping.Item1}:{mapping.Item2}");
      }

      if (Ulimit.Count > 0)
        foreach (var ulimit in Ulimit)
          sb.Append($" --ulimit {ulimit}");

      // Block IO bandwidth (Blkio) constraint
      if (null != BlockIoWeight)
        sb.Append($" --blkio-weight {BlockIoWeight.Value}");
      sb.OptionIfExists("--blkio-weight-device=", BlockIoWeightDevices);
      sb.OptionIfExists("--device-read-bps ", DeviceReadBps);
      sb.OptionIfExists("--device-read-iops=", DeviceReadIops);
      sb.OptionIfExists("--device-write-bps=", DeviceWriteBps);
      sb.OptionIfExists("--device-write-iops=", DeviceWriteIops);

      // Runtime privilege and Linux capabilities
      sb.OptionIfExists("--cap-add=", CapabilitiesToAdd);
      sb.OptionIfExists("--cap-drop=", CapabilitiesToRemove);
      if (Privileged)
        sb.Append(" --privileged");
      sb.OptionIfExists("--device=", Device);

      // Network settings
      sb.OptionIfExists("--dns=", Dns);
      sb.OptionIfExists("--dns-opt=", DnsOpt);
      sb.OptionIfExists("--dns-search=", DnsSearch);
      sb.OptionIfExists("--hostname ", Hostname);
      if (!PublishAllPorts)
        sb.OptionIfExists("-p ", PortMappings);
      else
        sb.Append(" -P");

      // Native health check
      sb.OptionIfExists("--health-cmd=", HealthCheckCmd);
      sb.OptionIfExists("--health-interval=", HealthCheckInterval);
      sb.OptionIfExists("--health-timeout=", HealthCheckTimeout);
      sb.OptionIfExists("--health-start-period=", HealthCheckStartPeriod);
      sb.OptionIfExists("--no-healthcheck", HealthCheckDisabled);

      if (HealthCheckRetries > 0)
        sb.Append($" --health-retries={HealthCheckRetries}");

      sb.OptionIfExists("--cgroup-parent ", ParentCGroup);
      sb.OptionIfExists("-e ", Environment);
      sb.OptionIfExists("--env-file=", EnvironmentFiles);

      if (Interactive)
        sb.Append(" -i");

      if (Tty)
        sb.Append(" -t");

      sb.OptionIfExists("-u ", AsUser);

      if (AutoRemoveContainer)
        sb.Append(" --rm");

      sb.OptionIfExists("--tmpfs ", TmpfsDestinations);
      sb.OptionIfExists("-v ", Volumes);
      sb.OptionIfExists("--volume-driver ", VolumeDriver);
      sb.OptionIfExists("--volumes-from=", VolumesFrom);
      sb.OptionIfExists("-w ", WorkingDirectory);

      sb.OptionIfExists("--link=", Links);
      sb.OptionIfExists("-l ", Labels);
      sb.OptionIfExists("--group-add=", Groups);
      sb.OptionIfExists("--network ", Network);
      if (!string.IsNullOrEmpty(Network))
      {
        sb.OptionIfExists("--network-alias ", Alias);
      }
      sb.OptionIfExists("--ip ", Ipv4);
      sb.OptionIfExists("--ip6 ", Ipv6);

      if (RestartPolicy.No != RestartPolicy)
        switch (RestartPolicy)
        {
          case RestartPolicy.Always:
            sb.Append(" --restart always");
            break;
          case RestartPolicy.OnFailure:
            sb.Append(" --restart on-failure");
            break;
          case RestartPolicy.UnlessStopped:
            sb.Append(" --restart unless-stopped");
            break;
          default:
            sb.Append(" --restart no");
            break;
        }

      // Memory management
      sb.SizeOptionIfValid("--memory=", Memory);
      sb.SizeOptionIfValid("--memory-swap=", MemorySwap);
      sb.OptionIfExists("--memory-swappiness=", MemorySwappiness);
      sb.SizeOptionIfValid("--memory-reservation=", MemoryReservation);
      sb.SizeOptionIfValid("--kernel-memory=", KernelMemory);
      if (OomKillDisable)
        sb.Append(" --oom-kill-disable");

      // Cpu management
      if (Cpus.HasValue)
        sb.Append($" --cpus=\"{Cpus.Value}\"");
      sb.OptionIfExists("--cpuset-cpus=", CpusetCpus);
      if (CpuShares.HasValue)
        sb.Append($" --cpu-shares=\"{CpuShares.Value}\"");

      // Runtime
      if (Runtime != ContainerRuntime.Default)
        sb.Append($" --runtime={Runtime.ToString().ToLower()}");

      var isolation = Isolation.ToDocker();
      if (null != isolation)
      {
        sb.Append($" --isolation {Isolation.ToDocker()}");
      }
      return sb.ToString();
    }
  }
}
