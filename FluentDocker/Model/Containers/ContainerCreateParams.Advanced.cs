using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using FluentDocker.Extensions;

namespace FluentDocker.Model.Containers
{
  public sealed partial class ContainerCreateParams
  {
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
    ///   Mount a tmpfs mount without any configurable options,
    ///   and it can only be used with standalone containers.
    /// </summary>
    /// <remarks>
    /// --tmpfs
    /// </remarks>
    public string[] TmpfsDestinations { get; set; }

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
    ///   Health check command for container
    /// </summary>
    /// <remarks>
    ///   This defines what command to run in order to check the health status.
    ///   Health check commands should return 0 if healthy and 1 if unhealthy.
    ///   Note that the command you use to validate health must be present in the image.
    ///   --health-cmd
    /// </remarks>
    public string HealthCheckCmd { get; set; }

    /// <summary>
    /// The timeout when the daemon deems a container as unhealthy
    /// </summary>
    /// <remarks>
    ///  If the health check command takes longer than this to complete,
    ///  it will be considered a failure. The default timeout is 30 seconds.
    ///  --health-timeout
    /// </remarks>
    public string HealthCheckTimeout { get; set; }

    /// <summary>
    /// The number of retries of the health check command <see cref="HealthCheckCmd"/>
    /// </summary>
    /// <remarks>
    ///   The health check will retry up to this many times before marking the container
    ///   as unhealthy. The default is 3 retries.
    ///   --health-retries
    /// </remarks>
    public int HealthCheckRetries { get; set; } = -1;

    /// <summary>
    /// The time between the <see cref="HealthCheckCmd"/> is executed.
    /// </summary>
    /// <remarks>
    ///   This controls the initial delay before the first health check runs and then how
    ///   often the health check command is executed thereafter. The default is 30 seconds.
    ///   --health-interval
    /// </remarks>
    public string HealthCheckInterval { get; set; }

    /// <summary>
    /// The start <see cref="HealthCheckInterval"/> for the first execution of <see cref="HealthCheckCmd"/>.
    /// </summary>
    /// <remarks>
    ///   The default is 30s.
    ///   --health-start-period
    /// </remarks>
    public string HealthCheckStartPeriod { get; set; }

    /// <summary>
    /// When set to true, independent on the HEALTHCHECK in the Dockerfile, no health check is performed.
    /// </summary>
    /// <remarks>
    /// --no-healthcheck
    /// </remarks>
    public bool HealthCheckDisabled { get; set; }
    /// <summary>
    ///   Publish a container's port(s) to the host
    /// </summary>
    /// <remarks>
    ///   By default, when you create a container, it does not publish any of its ports to the outside world.
    ///   To make a port available to services outside of Docker, or to Docker containers which are not connected
    ///   to the container's network, use the --publish or -p flag. This creates a firewall rule which maps a container
    ///   port to a port on the Docker host. Here are some examples.
    ///   <list type="table">
    ///     <listheader>
    ///       <term>Flag value</term>
    ///       <description>Description</description>
    ///     </listheader>
    ///     <item>
    ///       <term>8080:80</term>
    ///       <description>Map TCP port 80 in the container to port 8080 on the Docker host.</description>
    ///     </item>
    ///     <item>
    ///       <term>192.168.1.100:8080:80</term>
    ///       <description>
    ///         Map TCP port 80 in the container to port 8080 on the Docker host for connections to host IP
    ///         192.168.1.100.
    ///       </description>
    ///     </item>
    ///     <item>
    ///       <term>8080:80/udp</term>
    ///       <description>Map UDP port 80 in the container to port 8080 on the Docker host.</description>
    ///     </item>
    ///     <item>
    ///       <term>{ "8080:80/tcp", "8080:80/udp" }</term>
    ///       <description>
    ///         Map TCP port 80 in the container to TCP port 8080 on the Docker host, and map UDP port 80 in the
    ///         container to UDP port 8080 on the Docker host.
    ///       </description>
    ///     </item>
    ///   </list>
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
    ///   Container isolation technology
    /// </summary>
    /// <remarks>
    ///   --isolation
    /// </remarks>
    public ContainerIsolationTechnology Isolation { get; set; }

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
    ///   Container IPv4 address (e.g. 172.30.100.104).
    /// </summary>
    /// <remarks>
    ///   --ip
    /// </remarks>
    public string Ipv4 { get; set; }

    /// <summary>
    ///   Container IPv6 address (e.g. 2001:db8::33).
    /// </summary>
    /// <remarks>
    ///   --ip6
    /// </remarks>
    public string Ipv6 { get; set; }

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
    ///   Inside the container, tools like free report the host's available swap, not what's available inside the container.
    ///   Don't rely on the output of free or similar tools to determine whether swap is present.
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
    ///   limit, it does not guarantee that the container doesn't exceed the limit.
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
    ///   When you turn on any kernel memory limits, the host machine tracks "high water mark" statistics on a per-process
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
    ///   kill the host system's processes to free memory.
    /// </remarks>
    public bool OomKillDisable { get; set; }

    /// <summary>
    ///   Set ulimits in docker container.
    /// </summary>
    /// <remarks>
    ///   Since setting ulimit settings in a container requires extra privileges not available in the default container, you
    ///   can set these using the --ulimit flag. --ulimit is specified with a soft and hard limit as
    ///   such: type=soft limit[:hard limit]. Note: If you do not provide a hard limit, the soft limit will be used for both
    ///   values. If no ulimits are set, they will be inherited from the default ulimits set on the daemon. as option is
    ///   disabled now. In other words, the following script is not supported
    ///   The key is the ulimit name and the value is soft and optionally the hard limit.
    ///   --ulimit
    /// </remarks>
    public IList<ULimitItem> Ulimit { get; } = [];

    /// <summary>
    /// Specify a container runtime. Default is docker built-in.
    /// </summary>
    public ContainerRuntime Runtime { get; set; } = ContainerRuntime.Default;

  }
}
