using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Drivers.Podman
{
  /// <summary>
  /// Podman-specific driver interface for machine (VM) management.
  /// On macOS/Windows, Podman runs containers inside a Linux VM
  /// managed by <c>podman machine</c> commands.
  /// </summary>
  public interface IPodmanMachineDriver
  {
    /// <summary>Initializes a new machine VM.</summary>
    Task<CommandResponse<Unit>> InitAsync(
        DriverContext context, MachineInitConfig config,
        CancellationToken cancellationToken = default);

    /// <summary>Starts a machine VM.</summary>
    Task<CommandResponse<Unit>> StartAsync(
        DriverContext context, string name = null,
        CancellationToken cancellationToken = default);

    /// <summary>Stops a machine VM.</summary>
    Task<CommandResponse<Unit>> StopAsync(
        DriverContext context, string name = null,
        CancellationToken cancellationToken = default);

    /// <summary>Removes a machine VM.</summary>
    Task<CommandResponse<Unit>> RemoveAsync(
        DriverContext context, string name = null, bool force = false,
        CancellationToken cancellationToken = default);

    /// <summary>Lists all machines.</summary>
    Task<CommandResponse<IList<MachineInfo>>> ListAsync(
        DriverContext context,
        CancellationToken cancellationToken = default);

    /// <summary>Inspects a machine returning detailed information.</summary>
    Task<CommandResponse<MachineInspectResult>> InspectAsync(
        DriverContext context, string name = null,
        CancellationToken cancellationToken = default);

    /// <summary>Executes a command via SSH in a machine VM.</summary>
    Task<CommandResponse<string>> SshAsync(
        DriverContext context, string name = null, string command = null,
        CancellationToken cancellationToken = default);

    /// <summary>Modifies machine settings (CPU, memory, disk, rootful).</summary>
    Task<CommandResponse<Unit>> SetAsync(
        DriverContext context, MachineSetConfig config, string name = null,
        CancellationToken cancellationToken = default);

    /// <summary>Returns machine host and version information.</summary>
    Task<CommandResponse<MachineHostInfo>> InfoAsync(
        DriverContext context,
        CancellationToken cancellationToken = default);
  }

  /// <summary>
  /// Configuration for <c>podman machine init</c>.
  /// </summary>
  public class MachineInitConfig
  {
    /// <summary>Machine name (defaults to podman-machine-default if null).</summary>
    public string Name { get; set; }

    /// <summary>Number of CPUs for the VM.</summary>
    public int? Cpus { get; set; }

    /// <summary>Disk size in GiB.</summary>
    public int? DiskSizeGiB { get; set; }

    /// <summary>Memory in MiB.</summary>
    public int? MemoryMiB { get; set; }

    /// <summary>Enable rootful mode (default: rootless).</summary>
    public bool Rootful { get; set; }

    /// <summary>Custom VM image URL or path.</summary>
    public string Image { get; set; }

    /// <summary>SSH username for the VM.</summary>
    public string Username { get; set; }

    /// <summary>Volume mounts (e.g., "/host/path:/vm/path").</summary>
    public List<string> Volumes { get; set; } = new List<string>();

    /// <summary>Start the machine immediately after init.</summary>
    public bool Now { get; set; }
  }

  /// <summary>
  /// Configuration for <c>podman machine set</c>.
  /// </summary>
  public class MachineSetConfig
  {
    /// <summary>Number of CPUs.</summary>
    public int? Cpus { get; set; }

    /// <summary>Disk size in GiB.</summary>
    public int? DiskSizeGiB { get; set; }

    /// <summary>Memory in MiB.</summary>
    public int? MemoryMiB { get; set; }

    /// <summary>Toggle rootful/rootless mode.</summary>
    public bool? Rootful { get; set; }
  }

  /// <summary>
  /// Summary information about a machine from <c>podman machine list</c>.
  /// </summary>
  public class MachineInfo
  {
    /// <summary>Machine name.</summary>
    public string Name { get; set; }

    /// <summary>Whether this is the default machine.</summary>
    public bool Default { get; set; }

    /// <summary>Whether the machine is currently running.</summary>
    public bool Running { get; set; }

    /// <summary>Creation timestamp.</summary>
    public string Created { get; set; }

    /// <summary>Last time the machine was running.</summary>
    public string LastUp { get; set; }

    /// <summary>VM type (qemu, applehv, hyperv, wsl).</summary>
    public string VMType { get; set; }

    /// <summary>Number of CPUs.</summary>
    public int Cpus { get; set; }

    /// <summary>Memory in bytes (as reported by podman).</summary>
    public long Memory { get; set; }

    /// <summary>Disk size in bytes (as reported by podman).</summary>
    public long DiskSize { get; set; }
  }

  /// <summary>
  /// Detailed inspection result from <c>podman machine inspect</c>.
  /// </summary>
  public class MachineInspectResult
  {
    /// <summary>Machine name.</summary>
    public string Name { get; set; }

    /// <summary>Machine state (running, stopped, etc.).</summary>
    public string State { get; set; }

    /// <summary>Whether rootful mode is enabled.</summary>
    public bool Rootful { get; set; }

    /// <summary>Creation timestamp.</summary>
    public string Created { get; set; }

    /// <summary>Last time the machine was running.</summary>
    public string LastUp { get; set; }

    /// <summary>Configuration directory path.</summary>
    public string ConfigDir { get; set; }

    /// <summary>Machine resource allocation.</summary>
    public MachineResources Resources { get; set; }

    /// <summary>Connection info (socket path).</summary>
    public MachineConnectionInfo ConnectionInfo { get; set; }
  }

  /// <summary>Machine resource allocation.</summary>
  public class MachineResources
  {
    /// <summary>Number of CPUs.</summary>
    public int Cpus { get; set; }

    /// <summary>Memory in MiB.</summary>
    public int MemoryMiB { get; set; }

    /// <summary>Disk size in GiB.</summary>
    public int DiskSizeGiB { get; set; }
  }

  /// <summary>Machine connection information.</summary>
  public class MachineConnectionInfo
  {
    /// <summary>Path to the Podman API socket.</summary>
    public string PodmanSocketPath { get; set; }
  }

  /// <summary>
  /// Machine host and version information from <c>podman machine info</c>.
  /// </summary>
  public class MachineHostInfo
  {
    /// <summary>Host architecture (amd64, arm64, etc.).</summary>
    public string Arch { get; set; }

    /// <summary>Host OS.</summary>
    public string OS { get; set; }

    /// <summary>Name of the currently active machine.</summary>
    public string CurrentMachine { get; set; }

    /// <summary>VM type (qemu, applehv, hyperv, wsl).</summary>
    public string VMType { get; set; }

    /// <summary>Number of machines configured.</summary>
    public int NumberOfMachines { get; set; }

    /// <summary>Machine configuration directory.</summary>
    public string MachineConfigDir { get; set; }

    /// <summary>Podman API version.</summary>
    public string ApiVersion { get; set; }

    /// <summary>Podman version.</summary>
    public string Version { get; set; }
  }
}
