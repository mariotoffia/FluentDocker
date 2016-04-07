using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;

namespace Ductus.FluentDocker.Model
{
  public sealed class DockerRunArguments
  {
    /*
      -a, --attach=[]                 Attach to STDIN, STDOUT or STDERR
      --cpu-shares                    CPU shares (relative weight)
      --cidfile                       Write the container ID to the file
      --cpu-period                    Limit CPU CFS (Completely Fair Scheduler) period
      --cpu-quota                     Limit CPU CFS (Completely Fair Scheduler) quota
      --cpuset-cpus                   CPUs in which to allow execution (0-3, 0,1)
      --cpuset-mems                   MEMs in which to allow execution (0-3, 0,1)
      -d, --detach                    Run container in background and print container ID
      --detach-keys                   Override the key sequence for detaching a container
      --device=[]                     Add a host device to the container
      --device-read-bps=[]            Limit read rate (bytes per second) from a device
      --device-read-iops=[]           Limit read rate (IO per second) from a device
      --device-write-bps=[]           Limit write rate (bytes per second) to a device
      --device-write-iops=[]          Limit write rate (IO per second) to a device
      --disable-content-trust=true    Skip image verification
      --dns=[]                        Set custom DNS servers
      --dns-opt=[]                    Set DNS options
      --dns-search=[]                 Set custom DNS search domains
      --entrypoint                    Overwrite the default ENTRYPOINT of the image
      --expose=[]                     Expose a port or a range of ports
      --group-add=[]                  Add additional groups to join
      -h, --hostname                  Container host name
      --help                          Print usage
      --ip                            Container IPv4 address (e.g. 172.30.100.104)
      --ip6                           Container IPv6 address (e.g. 2001:db8::33)
      --ipc                           IPC namespace to use
      --isolation                     Container isolation level
      --kernel-memory                 Kernel memory limit
      -l, --label=[]                  Set meta data on a container
      --label-file=[]                 Read in a line delimited file of labels
      --link=[]                       Add link to another container
      --log-driver                    Logging driver for container
      --log-opt=[]                    Log driver options
      -m, --memory                    Memory limit
      --mac-address                   Container MAC address (e.g. 92:d0:c6:0a:29:33)
      --memory-reservation            Memory soft limit
      --memory-swap                   Swap limit equal to memory plus swap: '-1' to enable unlimited swap
      --memory-swappiness=-1          Tune container memory swappiness (0 to 100)
      --net=default                   Connect a container to a network
      --net-alias=[]                  Add network-scoped alias for the container
      --oom-kill-disable              Disable OOM Killer
      --oom-score-adj                 Tune host's OOM preferences (-1000 to 1000)
      -P, --publish-all               Publish all exposed ports to random ports
      -p, --publish=[]                Publish a container's port(s) to the host
      --pid                           PID namespace to use
      --privileged                    Give extended privileges to this container
      --read-only                     Mount the container's root filesystem as read only
      --restart=no                    Restart policy to apply when a container exits
      --security-opt=[]               Security Options
      --shm-size                      Size of /dev/shm, default value is 64MB
      --sig-proxy=true                Proxy received signals to the process
      --stop-signal=15                Signal to stop a container, 15 by default
      --tmpfs=[]                      Mount a tmpfs directory
      --ulimit=[]                     Ulimit options
      --uts                           UTS namespace to use
    */
    /// <summary>
    /// Add a custom host-to-IP mapping (host:ip).
    /// </summary>
    /// <remarks>
    /// --add-host=[]
    /// </remarks>
    public List<Tuple<string/*host*/,IPAddress/*mapsTo*/>> HostIpMappings { get; set; }
    /// <summary>
    /// Block IO (relative weight), between 10 and 1000
    /// </summary>
    /// <remarks>
    /// --blkio-weight 
    /// </remarks>
    public int ?BlockIoWeight { get; set; }
    /// <summary>
    /// Block IO weight(relative device weight)
    /// </summary>
    /// <remarks>
    /// --blkio-weight-device=[]
    /// </remarks>
    public string []BlockIoWeightDevices { get; set; }
    /// <summary>
    /// Add Linux capabilities
    /// </summary>
    /// <remarks>
    /// --cap-add=[]
    /// </remarks>
    public string []CapabilitiesToAdd { get; set; }
    /// <summary>
    /// Drop Linux capabilities
    /// </summary>
    /// <remarks>
    /// --cap-drop=[]
    /// </remarks>
    public string []CapabilitiesToRemove { get; set; }
    /// <summary>
    /// Optional parent cgroup for the container
    /// </summary>
    /// <remarks>
    /// --cgroup-parent
    /// </remarks>
    public string ParentCGroup { get; set; }
    /// <summary>
    /// Set environment variables
    /// </summary>
    /// <remarks>
    /// -e, --env=[]
    /// </remarks>
    public string []Environment { get; set; }
    /// <summary>
    /// Read in a file of environment variables
    /// </summary>
    /// <remarks>
    /// --env-file=[]
    /// </remarks>
    public string []EnvironmentFiles { get; set; }
    /// <summary>
    /// Keep STDIN open even if not attached
    /// </summary>
    /// <remarks>
    /// -i, --interactive
    /// </remarks>
    public bool Interactive { get; set; }
    /// <summary>
    /// Allocate a pseudo-TTY
    /// </summary>
    /// <remarks>
    /// -t, --tty
    /// </remarks>
    public bool Tty { get; set; }
    /// <summary>
    /// Assign a name to the container
    /// </summary>
    /// <remarks>
    /// --name
    /// </remarks>
    public string Name { get; set; }
    /// <summary>
    /// Username or UID (format: name|uid[:group|gid])
    /// </summary>
    /// <remarks>
    /// -u, --user
    /// </remarks>
    public string AsUser { get; set; }
    /// <summary>
    /// Automatically remove the container when it exits
    /// </summary>
    /// <remarks>
    /// --rm
    /// </remarks>
    public bool AutoRemoveContainer { get; set; }
    /// <summary>
    /// Bind mount a volume
    /// </summary>
    /// <remarks>
    /// -v, --volume=[]
    /// </remarks>
    public string []Volumes { get; set; }
    /// <summary>
    /// Optional volume driver for the container
    /// </summary>
    /// <remarks>
    /// --volume-driver
    /// </remarks>
    public string VolumeDriver { get; set; }
    /// <summary>
    /// Mount volumes from the specified container(s)
    /// </summary>
    /// <remarks>
    /// --volumes-from=[]
    /// </remarks>
    public string []VolumesFrom { get; set; }
    /// <summary>
    /// Working directory inside the container
    /// </summary>
    /// <remarks>
    /// -w, --workdir
    /// </remarks>
    public string WorkingDirectory { get; set; }

    /// <summary>
    /// Renders the argument string from this instance.
    /// </summary>
    /// <returns></returns>
    public override string ToString()
    {
      var sb = new StringBuilder();
      if (null != HostIpMappings && 0 != HostIpMappings.Count)
      {
        sb.Append(" --add-host=");
        foreach (var mapping in HostIpMappings)
        {
          sb.Append($"--add-host={mapping.Item1}:{mapping.Item2}");
        }
      }

      if (null != BlockIoWeight)
      {
       sb.Append($" --blkio-weight {BlockIoWeight.Value}");
      }

      RenderIfExists(sb, "--blkio-weight-device=", BlockIoWeightDevices);
      RenderIfExists(sb, "--cap-add=", CapabilitiesToAdd);
      RenderIfExists(sb, "--cap-drop=", CapabilitiesToRemove);
      RenderIfExists(sb, "--cgroup-parent ", ParentCGroup);
      RenderIfExists(sb, "-e ", Environment);
      RenderIfExists(sb, "--env-file=", EnvironmentFiles);

      if (Interactive)
      {
        sb.Append(" -i");
      }

      if (Tty)
      {
        sb.Append(" -t");
      }

      RenderIfExists(sb, "--name ", Name);
      RenderIfExists(sb,"-u ", AsUser);

      if (AutoRemoveContainer)
      {
        sb.Append(" --rm");
      }

      RenderIfExists(sb,"-v ", Volumes);
      RenderIfExists(sb, "--volume-driver ",VolumeDriver);
      RenderIfExists(sb, "--volumes-from=", VolumesFrom);
      RenderIfExists(sb,"-w ", WorkingDirectory);

      return sb.ToString();
    }

    private static void RenderIfExists(StringBuilder sb, string option, string value)
    {
      if (!string.IsNullOrEmpty(value))
      {
        sb.Append($" {option}{value}");
      }
    }
    private static void RenderIfExists(StringBuilder sb, string option, string[] values)
    {
      if (null == values || 0 == values.Length)
      {
        return;
      }

      foreach (var value in values)
      {
        sb.Append($" {option}{value}");
      }
    }
  }
}
