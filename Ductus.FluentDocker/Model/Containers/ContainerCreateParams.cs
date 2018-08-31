using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using Ductus.FluentDocker.Extensions;

namespace Ductus.FluentDocker.Model.Containers
{
  public sealed class ContainerCreateParams
  {
    /// <summary>
    ///   Specify how much of the available CPU resources a container can use.
    /// </summary>
    /// <remarks>
    ///   For instance, if the host machine has two CPUs and you set --cpus="1.5", the container is guaranteed at
    ///   most one and a half of the CPUs. If set to <see cref="float.MinValue" /> it will use the default.
    ///   See: https://docs.docker.com/config/containers/resource_constraints/#cpu
    /// </remarks>
    public float Cpus { get; set; } = float.MinValue;

    /// <summary>
    ///   Limit the specific CPUs or cores a container can use.
    /// </summary>
    /// <remarks>
    ///   A comma-separated list or hyphen-separated range of CPUs a container can use, if you have more than one CPU.
    ///   The first CPU is numbered 0. A valid value might be 0-3 (to use the first, second, third, and fourth CPU)
    ///   or 1,3 (to use the second and fourth CPU). --cpuset-cpus
    ///   See: https://docs.docker.com/config/containers/resource_constraints/#cpu
    /// </remarks>
    public string CpusetCpus { get; set; }

    /// <summary>
    ///   Set this flag to a value greater or less than the default of 1024 to increase or reduce the container’s weight,
    ///   and give it access to a greater or lesser proportion of the host machine’s CPU cycles.
    /// </summary>
    /// <remarks>
    ///   This is only enforced when CPU cycles are constrained. When plenty of CPU cycles are available,
    ///   all containers use as much CPU as they need. In that way, this is a soft limit. --cpu-shares does
    ///   not prevent containers from being scheduled in swarm mode. It prioritizes container CPU resources
    ///   for the available CPU cycles. It does not guarantee or reserve any specific CPU access.
    ///   Set the value to <see cref="int.MinValue" /> to use the default. --cpu-shares
    ///   See: https://docs.docker.com/config/containers/resource_constraints/#cpu
    /// </remarks>
    public int CpuShares { get; set; } = int.MinValue;

    /// <summary>
    ///   Write the container ID to the file
    /// </summary>
    /// <remarks>
    ///   To help with automation, you can have Docker write the container ID out to a file of your choosing.
    ///   This is similar to how some programs might write out their process ID to a file (you’ve seen them as PID files).
    ///   --cidfile
    /// </remarks>
    public string CidFile { get; set; }

    /// <summary>
    ///   PID namespace to use.
    /// </summary>
    /// <remarks>
    ///   By default, all containers have the PID namespace enabled.
    ///   PID namespace provides separation of processes. The PID Namespace removes the view of the system processes, and
    ///   allows process ids to be reused including pid 1.
    ///   In certain cases you want your container to share the host’s process namespace, basically allowing processes within
    ///   the container to see all of the processes on the system. For example, you could build a container with debugging
    ///   tools like strace or gdb, but want to use these tools when debugging processes within the container.
    ///   Example: run htop inside a container
    ///   Create this Dockerfile: FROM alpine:latest RUN apk add --update htop && rm -rf /var/cache/apk/* CMD ["htop"]
    ///   Build the Dockerfile and tag the image as myhtop: docker build -t myhtop .
    ///   Use the following command to run htop inside a container: docker run -it --rm --pid=host myhtop
    ///   --pid
    /// </remarks>
    public string Pid { get; set; }

    /// <summary>
    ///   Set the UTS namespace mode for the container.
    /// </summary>
    /// <remarks>
    ///   The UTS namespace is for setting the hostname and the domain that is visible to running processes in that namespace.
    ///   By default, all containers, including those with --network=host, have their own UTS namespace. The host setting will
    ///   result in the container using the same UTS namespace as the host. Note that --hostname is invalid in host UTS mode.
    ///   You may wish to share the UTS namespace with the host if you would like the hostname of the container to change as
    ///   the hostname of the host changes. A more advanced use case would be changing the host’s hostname from a container.
    ///   --uts
    /// </remarks>
    public string Uts { get; set; }

    /// <summary>
    ///   Set the IPC mode for the container.
    /// </summary>
    /// <remarks>
    ///   The following values are accepted:
    ///   “none” - Own private IPC namespace, with /dev/shm not mounted.
    ///   “private” - Own private IPC namespace.
    ///   “shareable” - Own private IPC namespace, with a possibility to share it with other containers.
    ///   “container: name-or-ID" - Join another (“shareable”) container’s IPC namespace.
    ///   “host” - Use the host system’s IPC namespace.
    ///   If not specified, daemon default is used, which can either be "private" or "shareable", depending on the daemon
    ///   version and configuration.
    /// 
    ///   IPC (POSIX/SysV IPC) namespace provides separation of named shared memory segments, semaphores and message queues.
    ///   Shared memory segments are used to accelerate inter-process communication at memory speed, rather than through pipes
    ///   or through the network stack. Shared memory is commonly used by databases and custom-built (typically C/OpenMPI,
    ///   C++/using boost libraries) high performance applications for scientific computing and financial services industries.
    ///   If these types of applications are broken into multiple containers, you might need to share the IPC mechanisms of the
    ///   containers, using "shareable" mode for the main (i.e. “donor”) container, and "container: donor-name-or-ID" for
    ///   other containers.
    ///     --ipc
    /// </remarks>
    public string Ipc { get; set; }

    /// <summary>
    ///   Add a custom host-to-IP mapping (host:ip).
    /// </summary>
    /// <remarks>
    ///   --add-host=[]
    /// </remarks>
    public List<Tuple<string, IPAddress>> HostIpMappings { get; set; }

    /// <summary>
    ///   Block IO (relative weight), between 10 and 1000
    /// </summary>
    /// <remarks>
    ///   --blkio-weight
    /// </remarks>
    public int? BlockIoWeight { get; set; }

    /// <summary>
    ///   Block IO weight(relative device weight)
    /// </summary>
    /// <remarks>
    ///   --blkio-weight-device=[]
    /// </remarks>
    public string[] BlockIoWeightDevices { get; set; }

    /// <summary>
    ///   Add Linux capabilities
    /// </summary>
    /// <remarks>
    ///   --cap-add=[]
    /// </remarks>
    public string[] CapabilitiesToAdd { get; set; }

    /// <summary>
    ///   Drop Linux capabilities
    /// </summary>
    /// <remarks>
    ///   --cap-drop=[]
    /// </remarks>
    public string[] CapabilitiesToRemove { get; set; }

    /// <summary>
    ///   Optional parent cgroup for the container
    /// </summary>
    /// <remarks>
    ///   --cgroup-parent
    /// </remarks>
    public string ParentCGroup { get; set; }

    /// <summary>
    ///   Set environment variables
    /// </summary>
    /// <remarks>
    ///   -e, --env=[]
    /// </remarks>
    public string[] Environment { get; set; }

    /// <summary>
    ///   Read in a file of environment variables
    /// </summary>
    /// <remarks>
    ///   --env-file=[]
    /// </remarks>
    public string[] EnvironmentFiles { get; set; }

    /// <summary>
    ///   Keep STDIN open even if not attached
    /// </summary>
    /// <remarks>
    ///   -i, --interactive
    /// </remarks>
    public bool Interactive { get; set; }

    /// <summary>
    ///   Allocate a pseudo-TTY
    /// </summary>
    /// <remarks>
    ///   -t, --tty
    /// </remarks>
    public bool Tty { get; set; }

    /// <summary>
    ///   Assign a name to the container
    /// </summary>
    /// <remarks>
    ///   --name
    /// </remarks>
    public string Name { get; set; }

    /// <summary>
    ///   Username or UID (format: name|uid[:group|gid])
    /// </summary>
    /// <remarks>
    ///   -u, --user
    /// </remarks>
    public string AsUser { get; set; }

    /// <summary>
    ///   Automatically remove the container when it exits
    /// </summary>
    /// <remarks>
    ///   --rm
    /// </remarks>
    public bool AutoRemoveContainer { get; set; }

    /// <summary>
    ///   Bind mount a volume
    /// </summary>
    /// <remarks>
    ///   -v, --volume=[]
    /// </remarks>
    public string[] Volumes { get; set; }

    /// <summary>
    ///   Optional volume driver for the container
    /// </summary>
    /// <remarks>
    ///   --volume-driver
    /// </remarks>
    public string VolumeDriver { get; set; }

    /// <summary>
    ///   Mount volumes from the specified container(s)
    /// </summary>
    /// <remarks>
    ///   --volumes-from=[]
    /// </remarks>
    public string[] VolumesFrom { get; set; }

    /// <summary>
    ///   Working directory inside the container
    /// </summary>
    /// <remarks>
    ///   -w, --workdir
    /// </remarks>
    public string WorkingDirectory { get; set; }

    /// <summary>
    ///   Publish a container's port(s) to the host
    /// </summary>
    /// <remarks>
    ///   -p, --publish=[]
    /// </remarks>
    public string[] PortMappings { get; set; }

    /// <summary>
    ///   Publish all exposed ports to random ports
    /// </summary>
    /// <remarks>
    ///   -P, --publish-all. This is mutual exclusive when set to true,
    ///   the <see cref="PortMappings" /> is ignored.
    /// </remarks>
    public bool PublishAllPorts { get; set; }

    /// <summary>
    ///   Add link to another container
    /// </summary>
    /// <remarks>
    ///   --link=[]
    /// </remarks>
    public string[] Links { get; set; }

    /// <summary>
    ///   Set meta data on a container
    /// </summary>
    /// <remarks>
    ///   -l, --label=[]
    /// </remarks>
    public string[] Labels { get; set; }

    /// <summary>
    ///   Add additional groups to join
    /// </summary>
    /// <remarks>
    ///   --group-add=[]
    /// </remarks>
    public string[] Groups { get; set; }

    /// <summary>
    ///   Connect a container to a network
    /// </summary>
    /// <remarks>
    ///   --network
    /// </remarks>
    public string Network { get; set; }

    /// <summary>
    ///   Restart policy for this container.
    /// </summary>
    /// <remarks>
    ///   --restart
    /// </remarks>
    public RestartPolicy RestartPolicy { get; set; }

    /// <summary>
    ///   The maximum amount of memory the container can use. If you set this option, the minimum allowed value is 4m (4
    ///   megabyte).
    /// </summary>
    /// <remarks>
    ///   -m, --memory=
    /// </remarks>
    public string Memory { get; set; }

    /// <summary>
    ///   The amount of memory this container is allowed to swap to disk.
    /// </summary>
    /// <remarks>
    ///   --memory-swap *
    ///   --memory-swap is a modifier flag that only has meaning if --memory is also set. Using swap allows the container to
    ///   write excess memory requirements to disk when the container has exhausted all the RAM that is available to it. There
    ///   is a performance penalty for applications that swap memory to disk often.
    ///   Its setting can have complicated effects:
    ///   If --memory-swap is set to a positive integer, then both --memory and --memory-swap must be set. --memory-swap
    ///   represents the total amount of memory and swap that can be used, and --memory controls the amount used by non-swap
    ///   memory. So if --memory="300m" and --memory-swap="1g", the container can use 300m of memory and 700m (1g - 300m) swap.
    ///   If --memory-swap is set to 0, the setting is ignored, and the value is treated as unset.
    ///   If --memory-swap is set to the same value as --memory, and --memory is set to a positive integer, the container does
    ///   not have access to swap. See Prevent a container from using swap.
    ///   If --memory-swap is unset, and --memory is set, the container can use twice as much swap as the --memory setting, if
    ///   the host container has swap memory configured. For instance, if --memory="300m" and --memory-swap is not set, the
    ///   container can use 300m of memory and 600m of swap.
    ///   If --memory-swap is explicitly set to -1, the container is allowed to use unlimited swap, up to the amount available
    ///   on the host system.
    ///   Inside the container, tools like free report the host’s available swap, not what’s available inside the container.
    ///   Don’t rely on the output of free or similar tools to determine whether swap is present.
    /// </remarks>
    public string MemorySwap { get; set; }

    /// <summary>
    ///   Tune container memory swappiness (0 to 100)
    /// </summary>
    /// <remarks>
    ///   By default, the host kernel can swap out a percentage of anonymous pages used by a container. You can set
    ///   --memory-swappiness to a value between 0 and 100, to tune this percentage.
    ///   A value of 0 turns off anonymous page swapping.
    ///   A value of 100 sets all anonymous pages as swappable.
    ///   By default, if you do not set --memory-swappiness, the value is inherited from the host machine.
    /// </remarks>
    public short? MemorySwappiness { get; set; }

    /// <summary>
    ///   Allows you to specify a soft limit smaller than --memory which is activated when Docker detects contention or low
    ///   memory on the host machine.
    /// </summary>
    /// <remarks>
    ///   If you use --memory-reservation, it must be set lower than --memory for it to take precedence. Because it is a soft
    ///   limit, it does not guarantee that the container doesn’t exceed the limit.
    /// </remarks>
    public string MemoryReservation { get; set; }

    /// <summary>
    ///   The maximum amount of kernel memory the container can use.
    /// </summary>
    /// <remarks>
    ///   The minimum allowed value is 4m. Because kernel memory cannot be swapped out, a container which is starved of kernel
    ///   memory may block host machine resources, which can have side effects on the host machine and on other containers.
    ///   Kernel memory limits are expressed in terms of the overall memory allocated to a container. Consider the following
    ///   scenarios:
    ///   Unlimited memory, unlimited kernel memory: This is the default behavior.
    ///   Unlimited memory, limited kernel memory: This is appropriate when the amount of memory needed by all cgroups is
    ///   greater than the amount of memory that actually exists on the host machine.You can configure the kernel memory to
    ///   never go over what is available on the host machine, and containers which need more memory need to wait for it.
    ///   Limited memory, unlimited kernel memory: The overall memory is limited, but the kernel memory is not.
    ///   Limited memory, limited kernel memory: Limiting both user and kernel memory can be useful for debugging
    ///   memory-related problems.If a container is using an unexpected amount of either type of memory, it runs out of memory
    ///   without affecting other containers or the host machine.Within this setting, if the kernel memory limit is lower than
    ///   the user memory limit, running out of kernel memory causes the container to experience an OOM error.If the kernel
    ///   memory limit is higher than the user memory limit, the kernel limit does not cause the container to experience an
    ///   OOM.
    ///   When you turn on any kernel memory limits, the host machine tracks “high water mark” statistics on a per-process
    ///   basis, so you can track which processes (in this case, containers) are using excess memory.This can be seen per
    ///   process by viewing /proc/PID/status on the host machine.
    /// </remarks>
    public string KernelMemory { get; set; }

    /// <summary>
    ///   By default, if an out-of-memory (OOM) error occurs, the kernel kills processes in a container.
    /// </summary>
    /// <remarks>
    ///   To change this behavior, use the --oom-kill-disable option. Only disable the OOM killer on containers where you have
    ///   also set the -m/--memory option. If the -m flag is not set, the host can run out of memory and the kernel may need to
    ///   kill the host system’s processes to free memory.
    /// </remarks>
    public bool OomKillDisable { get; set; }

    /// <summary>
    ///   Renders the argument string from this instance.
    /// </summary>
    /// <returns></returns>
    public override string ToString()
    {
      var sb = new StringBuilder();
      
      // Container identification
      sb.OptionIfExists("--name ", Name);
      sb.OptionIfExists("--pid=", Pid);
      sb.OptionIfExists("--uts=", Uts);
      sb.OptionIfExists("--ipc=", Ipc);
      if (!string.IsNullOrWhiteSpace(CidFile)) sb.Append($" --cidfile=\"{CidFile}\"");
      
      if (null != HostIpMappings && 0 != HostIpMappings.Count)
      {
        sb.Append(" --add-host=");
        foreach (var mapping in HostIpMappings) sb.Append($"--add-host={mapping.Item1}:{mapping.Item2}");
      }

      if (null != BlockIoWeight) sb.Append($" --blkio-weight {BlockIoWeight.Value}");

      sb.OptionIfExists("--blkio-weight-device=", BlockIoWeightDevices);
      sb.OptionIfExists("--cap-add=", CapabilitiesToAdd);
      sb.OptionIfExists("--cap-drop=", CapabilitiesToRemove);
      sb.OptionIfExists("--cgroup-parent ", ParentCGroup);
      sb.OptionIfExists("-e ", Environment);
      sb.OptionIfExists("--env-file=", EnvironmentFiles);

      if (Interactive) sb.Append(" -i");

      if (Tty) sb.Append(" -t");

      sb.OptionIfExists("-u ", AsUser);

      if (AutoRemoveContainer) sb.Append(" --rm");

      sb.OptionIfExists("-v ", Volumes);
      sb.OptionIfExists("--volume-driver ", VolumeDriver);
      sb.OptionIfExists("--volumes-from=", VolumesFrom);
      sb.OptionIfExists("-w ", WorkingDirectory);

      if (!PublishAllPorts)
        sb.OptionIfExists("-p ", PortMappings);
      else
        sb.Append(" -P");

      sb.OptionIfExists("--link=", Links);
      sb.OptionIfExists("-l ", Labels);
      sb.OptionIfExists("--group-add=", Groups);
      sb.OptionIfExists("--network ", Network);

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
      sb.SizeOptionIfValid("--memory=", Memory, 4 * 1024 * 1024 /*4m*/);
      sb.SizeOptionIfValid("--memory-swap=", MemorySwap);
      sb.OptionIfExists("--memory-swappiness=", MemorySwappiness);
      sb.SizeOptionIfValid("--memory-reservation=", MemoryReservation);
      sb.SizeOptionIfValid("--kernel-memory=", KernelMemory);
      if (OomKillDisable) sb.Append(" --oom-kill-disable");

      // Cpu management
      if (Cpus.IsApproximatelyEqualTo(float.MinValue)) sb.Append($" --cpus=\"{Cpus}\"");
      sb.OptionIfExists("--cpuset-cpus=", CpusetCpus);
      if (CpuShares != int.MinValue) sb.Append($" --cpu-shares=\"{CpuShares}\"");


      return sb.ToString();
    }
  }

  /*
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
  -h, --hostname                  Container host name
  --ip                            Container IPv4 address (e.g. 172.30.100.104)
  --ip6                           Container IPv6 address (e.g. 2001:db8::33)
  --isolation                     Container isolation level
  --label-file=[]                 Read in a line delimited file of labels
  --log-driver                    Logging driver for container
  --log-opt=[]                    Log driver options
  --mac-address                   Container MAC address (e.g. 92:d0:c6:0a:29:33)
  --net=default                   Connect a container to a network
  --net-alias=[]                  Add network-scoped alias for the container
  --oom-score-adj                 Tune host's OOM preferences (-1000 to 1000)  
  --privileged                    Give extended privileges to this container
  --read-only                     Mount the container's root filesystem as read only
  --security-opt=[]               Security Options
  --shm-size                      Size of /dev/shm, default value is 64MB
  --sig-proxy=true                Proxy received signals to the process
  --stop-signal=15                Signal to stop a container, 15 by default
  --tmpfs=[]                      Mount a tmpfs directory
  --ulimit=[]                     Ulimit options
*/
}