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
    ///   The alias to use then connecting the container to a network. This is not used if
    ///   <see cref="Network"/> is not set.
    /// </summary>
    /// <remarks>
    ///   --alias
    /// </remarks>
    public string Alias { get; set; }


    /// <summary>
    ///   Specify how much of the available CPU resources a container can use.
    /// </summary>
    /// <remarks>
    ///   For instance, if the host machine has two CPUs and you set --cpus="1.5", the container is guaranteed at
    ///   most one and a half of the CPUs. When null, the Docker default is used.
    ///   See: https://docs.docker.com/config/containers/resource_constraints/#cpu
    /// </remarks>
    public float? Cpus { get; set; }

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
    ///   When null, the Docker default (1024) is used. --cpu-shares
    ///   See: https://docs.docker.com/config/containers/resource_constraints/#cpu
    /// </remarks>
    public int? CpuShares { get; set; }

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
    ///   Create this Dockerfile: FROM alpine:latest RUN apk add --update htop &amp;&amp; rm -rf /var/cache/apk/* CMD ["htop"]
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
    ///   IPC (POSIX/SysV IPC) namespace provides separation of named shared memory segments, semaphores and message queues.
    ///   Shared memory segments are used to accelerate inter-process communication at memory speed, rather than through pipes
    ///   or through the network stack. Shared memory is commonly used by databases and custom-built (typically C/OpenMPI,
    ///   C++/using boost libraries) high performance applications for scientific computing and financial services industries.
    ///   If these types of applications are broken into multiple containers, you might need to share the IPC mechanisms of the
    ///   containers, using "shareable" mode for the main (i.e. “donor”) container, and "container: donor-name-or-ID" for
    ///   other containers.
    ///   --ipc
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
    ///   The --device-write-bps flag limits the write rate (bytes per second)to a device.
    /// </summary>
    /// <remarks>
    ///   For example, this command creates a container and limits the write rate to 1mb per second for /dev/sda:
    ///   docker run -it --device-write-bps /dev/sda:1mb ubuntu
    ///   --device-read-bps
    /// </remarks>
    public string DeviceReadBps { get; set; }

    /// <summary>
    ///   he --device-read-iops flag limits read rate (IO per second) from a device.
    /// </summary>
    /// <remarks>
    ///   For example, this command creates a container and limits the read rate to 1000 IO per second from /dev/sda:
    ///   docker run -ti --device-read-iops /dev/sda:1000 ubuntu.
    ///   Limits are specified in the device-path:limit format and rates must be a positive integer.
    ///   --device-read-iops=[]
    /// </remarks>
    public string DeviceReadIops { get; set; }

    /// <summary>
    ///   The --device-write-bps flag limits the write rate (bytes per second)to a device.
    /// </summary>
    /// <remarks>
    ///   For example, this command creates a container and limits the write rate to 1mb per second for /dev/sda:
    ///   docker run -it --device-write-bps /dev/sda:1mb ubuntu
    ///   --device-write-bps=[]
    /// </remarks>
    public string DeviceWriteBps { get; set; }

    /// <summary>
    ///   The --device-write-iops flag limits write rate (IO per second) to a device.
    /// </summary>
    /// <remarks>
    ///   For example, this command creates a container and limits the write rate to 1000 IO per second to /dev/sda:
    ///   docker run -ti --device-write-iops /dev/sda:1000 ubuntu
    ///   Limits are specified in the device-path:limit format and rates must be a positive integer.
    ///   --device-write-iops=[]
    /// </remarks>
    public string DeviceWriteIops { get; set; }

    /// <summary>
    ///   Add Linux capabilities
    /// </summary>
    /// <remarks>
    ///   By default, Docker has a default list of capabilities that are kept. The following table lists the
    ///   Linux capability options which are allowed by default and can be dropped.
    ///   <list type="table">
    ///     <listheader>
    ///       <term>Capability Key</term>
    ///       <description>Capability Description</description>
    ///     </listheader>
    ///     <item>
    ///       <term>SETPCAP</term>
    ///       <description>Modify process capabilities.</description>
    ///     </item>
    ///     <item>
    ///       <term>MKNOD</term>
    ///       <description>Create special files using mknod(2).</description>
    ///     </item>
    ///     <item>
    ///       <term>AUDIT_WRITE</term>
    ///       <description>Write records to kernel auditing log.</description>
    ///     </item>
    ///     <item>
    ///       <term>CHOWN</term>
    ///       <description>Make arbitrary changes to file UIDs and GIDs (see chown(2)).</description>
    ///     </item>
    ///     <item>
    ///       <term>NET_RAW</term>
    ///       <description>Use RAW and PACKET sockets.</description>
    ///     </item>
    ///     <item>
    ///       <term>DAC_OVERRIDE</term>
    ///       <description>Bypass file read, write, and execute permission checks.</description>
    ///     </item>
    ///     <item>
    ///       <term>FOWNER</term>
    ///       <description>
    ///         Bypass permission checks on operations that normally require the file system UID of the process to
    ///         match the UID of the file.
    ///       </description>
    ///     </item>
    ///     <item>
    ///       <term>FSETID</term>
    ///       <description>Don’t clear set-user-ID and set-group-ID permission bits when a file is modified.</description>
    ///     </item>
    ///     <item>
    ///       <term>KILL</term>
    ///       <description>Bypass permission checks for sending signals.</description>
    ///     </item>
    ///     <item>
    ///       <term>SETGID</term>
    ///       <description>Make arbitrary manipulations of process GIDs and supplementary GID list.</description>
    ///     </item>
    ///     <item>
    ///       <term>SETUID</term>
    ///       <description>Make arbitrary manipulations of process UIDs.</description>
    ///     </item>
    ///     <item>
    ///       <term>NET_BIND_SERVICE</term>
    ///       <description>Bind a socket to internet domain privileged ports (port numbers less than 1024).</description>
    ///     </item>
    ///     <item>
    ///       <term>SYS_CHROOT</term>
    ///       <description>Use chroot(2), change root directory.</description>
    ///     </item>
    ///     <item>
    ///       <term>SETFCAP</term>
    ///       <description>Set file capabilities.</description>
    ///     </item>
    ///     <item>
    ///       <term>SETPCAP</term>
    ///       <description>DESCRIPTION</description>
    ///     </item>
    ///   </list>
    ///   The next table shows the capabilities which are not granted by default and may be added.
    ///   <list type="table">
    ///     <listheader>
    ///       <term>Capability Key</term>
    ///       <description>Capability Description</description>
    ///     </listheader>
    ///     <item>
    ///       <term>SYS_MODULE</term>
    ///       <description>Load and unload kernel modules.</description>
    ///     </item>
    ///     <item>
    ///       <term>SYS_RAWIO</term>
    ///       <description>Perform I/O port operations (iopl(2) and ioperm(2)).</description>
    ///     </item>
    ///     <item>
    ///       <term>SYS_PACCT</term>
    ///       <description>Use acct(2), switch process accounting on or off.</description>
    ///     </item>
    ///     <item>
    ///       <term>SYS_ADMIN</term>
    ///       <description>Perform a range of system administration operations.</description>
    ///     </item>
    ///     <item>
    ///       <term>SYS_NICE</term>
    ///       <description>
    ///         Raise process nice value (nice(2), setpriority(2)) and change the nice value for arbitrary
    ///         processes.
    ///       </description>
    ///     </item>
    ///     <item>
    ///       <term>SYS_RESOURCE</term>
    ///       <description>Override resource Limits.</description>
    ///     </item>
    ///     <item>
    ///       <term>SYS_TIME</term>
    ///       <description>Set system clock (settimeofday(2), stime(2), adjtimex(2)); set real-time (hardware) clock.</description>
    ///     </item>
    ///     <item>
    ///       <term>SYS_TTY_CONFIG</term>
    ///       <description>Use vhangup(2); employ various privileged ioctl(2) operations on virtual terminals.</description>
    ///     </item>
    ///     <item>
    ///       <term>AUDIT_CONTROL</term>
    ///       <description>
    ///         Enable and disable kernel auditing; change auditing filter rules; retrieve auditing status and
    ///         filtering rules.
    ///       </description>
    ///     </item>
    ///     <item>
    ///       <term>MAC_ADMIN</term>
    ///       <description>Allow MAC configuration or state changes. Implemented for the Smack LSM.</description>
    ///     </item>
    ///     <item>
    ///       <term>MAC_OVERRIDE</term>
    ///       <description>Override Mandatory Access Control (MAC). Implemented for the Smack Linux Security Module (LSM).</description>
    ///     </item>
    ///     <item>
    ///       <term>NET_ADMIN</term>
    ///       <description>Perform various network-related operations.</description>
    ///     </item>
    ///     <item>
    ///       <term>SYSLOG</term>
    ///       <description>Perform privileged syslog(2) operations.</description>
    ///     </item>
    ///     <item>
    ///       <term>DAC_READ_SEARCH</term>
    ///       <description>Bypass file read permission checks and directory read and execute permission checks.</description>
    ///     </item>
    ///     <item>
    ///       <term>LINUX_IMMUTABLE</term>
    ///       <description>Set the FS_APPEND_FL and FS_IMMUTABLE_FL i-node flags.</description>
    ///     </item>
    ///     <item>
    ///       <term>NET_BROADCAST</term>
    ///       <description>Make socket broadcasts, and listen to multicasts.</description>
    ///     </item>
    ///     <item>
    ///       <term>IPC_LOCK</term>
    ///       <description>Lock memory (mlock(2), mlockall(2), mmap(2), shmctl(2)).</description>
    ///     </item>
    ///     <item>
    ///       <term>IPC_OWNER</term>
    ///       <description>Bypass permission checks for operations on System V IPC objects.</description>
    ///     </item>
    ///     <item>
    ///       <term>SYS_PTRACE</term>
    ///       <description>Trace arbitrary processes using ptrace(2).</description>
    ///     </item>
    ///     <item>
    ///       <term>SYS_BOOT</term>
    ///       <description>Use reboot(2) and kexec_load(2), reboot and load a new kernel for later execution.</description>
    ///     </item>
    ///     <item>
    ///       <term>LEASE</term>
    ///       <description>Establish leases on arbitrary files (see fcntl(2)).</description>
    ///     </item>
    ///     <item>
    ///       <term>WAKE_ALARM</term>
    ///       <description>Trigger something that will wake up the system.</description>
    ///     </item>
    ///     <item>
    ///       <term>BLOCK_SUSPEND</term>
    ///       <description>Employ features that can block system suspend.</description>
    ///     </item>
    ///   </list>
    ///   Further reference information is available on the capabilities(7) - Linux man page
    ///   Both flags support the value ALL, so if the operator wants to have all capabilities but MKNOD they could use:
    ///   docker run --cap-add=ALL --cap-drop=MKNOD ...
    ///   For interacting with the network stack, instead of using --privileged they should use --cap-add=NET_ADMIN to modify
    ///   the network interfaces.
    ///   docker run -it --rm  ubuntu:14.04 ip link add dummy0 type dummy
    ///   RTNETLINK answers: Operation not permitted
    ///   docker run -it --rm --cap-add=NET_ADMIN ubuntu:14.04 ip link add dummy0 type dummy
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
    ///   Give extended privileges to this container.
    /// </summary>
    /// <remarks>
    ///   When the operator executes docker run --privileged, Docker will enable access to all devices on
    ///   the host as well as set some configuration in AppArmor or SELinux to allow the container nearly
    ///   all the same access to the host as processes running outside containers on the host.
    ///   --privileged
    /// </remarks>
    public bool Privileged { get; set; }

    /// <summary>
    ///   Allows you to run devices inside the container without the --privileged flag.
    /// </summary>
    /// <remarks>
    ///   If you want to limit access to a specific device or devices you can use the --device flag.
    ///   It allows you to specify one or more devices that will be accessible within the container.
    ///   docker run --device=/dev/snd:/dev/snd ...
    ///   By default, the container will be able to read, write, and mknod these devices.
    ///   This can be overridden using a third :rwm set of options to each --device flag:
    ///   --device=[]
    /// </remarks>
    public string Device { get; set; }

    /// <summary>
    ///   Optional parent cgroup for the container
    /// </summary>
    /// <remarks>
    ///   --cgroup-parent
    /// </remarks>
    public string ParentCGroup { get; set; }

    /// <summary>
    ///   Your container will use the same DNS servers as the host by default, but you can override this with --dns.
    /// </summary>
    /// <remarks>
    ///   The IP address of a DNS server. To specify multiple DNS servers, use multiple --dns flags. If
    ///   the container cannot reach any of the IP addresses you specify, Google’s public DNS server
    ///   8.8.8.8 is added, so that your container can resolve internet domains.
    ///   --dns=[]
    /// </remarks>
    public string[] Dns { get; set; }

    /// <summary>
    ///   A key-value pair representing a DNS option and its value.
    /// </summary>
    /// <remarks>
    ///   See your operating system’s documentation for resolv.conf for valid options.
    ///   --dns-opt=[]
    /// </remarks>
    public string[] DnsOpt { get; set; }

    /// <summary>
    ///   A DNS search domain to search non-fully-qualified hostnames.
    /// </summary>
    /// <remarks>
    ///   It is possible to specify multiple DNS search prefixes.
    ///   --dns-search=[]
    /// </remarks>
    public string[] DnsSearch { get; set; }

    /// <summary>
    ///   The hostname a container uses for itself.
    /// </summary>
    /// <remarks>
    ///   Defaults to the container’s name if not specified.
    ///   --hostname
    /// </remarks>
    public string Hostname { get; set; }

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

  }
}
