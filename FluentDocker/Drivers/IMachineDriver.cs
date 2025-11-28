using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Model.Drivers;
using FluentDocker.Services;

namespace FluentDocker.Drivers
{
    /// <summary>
    /// Docker Machine management driver (legacy but still in use).
    /// Supported by: Docker only
    /// Not supported by: Podman, Kubernetes
    /// </summary>
    /// <remarks>
    /// Docker Machine is deprecated but still used in some environments,
    /// particularly for managing VMs on Hyper-V, VirtualBox, etc.
    /// </remarks>
    public interface IMachineDriver
    {
        /// <summary>
        /// Checks if Docker Machine is available on the system.
        /// </summary>
        /// <returns>True if docker-machine binary is available</returns>
        bool IsAvailable();

        /// <summary>
        /// Lists all machines.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>List of machines</returns>
        Task<CommandResponse<IList<MachineInfo>>> ListAsync(
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Inspects a machine to get detailed information.
        /// </summary>
        /// <param name="machineName">Machine name</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Machine details</returns>
        Task<CommandResponse<MachineDetails>> InspectAsync(
            string machineName,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Starts a machine.
        /// </summary>
        /// <param name="machineName">Machine name</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task<CommandResponse<Unit>> StartAsync(
            string machineName,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Stops a machine.
        /// </summary>
        /// <param name="machineName">Machine name</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task<CommandResponse<Unit>> StopAsync(
            string machineName,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Restarts a machine.
        /// </summary>
        /// <param name="machineName">Machine name</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task<CommandResponse<Unit>> RestartAsync(
            string machineName,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates a new machine.
        /// </summary>
        /// <param name="config">Machine creation configuration</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task<CommandResponse<Unit>> CreateAsync(
            MachineCreateConfig config,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes a machine.
        /// </summary>
        /// <param name="machineName">Machine name</param>
        /// <param name="force">Force deletion</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task<CommandResponse<Unit>> DeleteAsync(
            string machineName,
            bool force = false,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets environment variables for a machine.
        /// </summary>
        /// <param name="machineName">Machine name</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Dictionary of environment variables</returns>
        Task<CommandResponse<Dictionary<string, string>>> GetEnvAsync(
            string machineName,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the URL of a machine's Docker daemon.
        /// </summary>
        /// <param name="machineName">Machine name</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Docker URL</returns>
        Task<CommandResponse<string>> GetUrlAsync(
            string machineName,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the status of a machine.
        /// </summary>
        /// <param name="machineName">Machine name</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Machine status</returns>
        Task<CommandResponse<ServiceRunningState>> GetStatusAsync(
            string machineName,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Regenerates TLS certificates for a machine.
        /// </summary>
        /// <param name="machineName">Machine name</param>
        /// <param name="force">Force regeneration</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task<CommandResponse<Unit>> RegenerateCertsAsync(
            string machineName,
            bool force = false,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Upgrades a machine to the latest version of Docker.
        /// </summary>
        /// <param name="machineName">Machine name</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task<CommandResponse<Unit>> UpgradeAsync(
            string machineName,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Runs SSH command on a machine.
        /// </summary>
        /// <param name="machineName">Machine name</param>
        /// <param name="command">Command to run</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Command output</returns>
        Task<CommandResponse<string>> SshAsync(
            string machineName,
            string command,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Copies files between local machine and Docker machine.
        /// </summary>
        /// <param name="source">Source path</param>
        /// <param name="destination">Destination path</param>
        /// <param name="recursive">Copy recursively</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task<CommandResponse<Unit>> ScpAsync(
            string source,
            string destination,
            bool recursive = false,
            CancellationToken cancellationToken = default);
    }

    #region Info Types

    /// <summary>
    /// Represents a Docker Machine.
    /// </summary>
    public class MachineInfo
    {
        /// <summary>Machine name.</summary>
        public string Name { get; set; }

        /// <summary>Machine state (Running, Stopped, etc.).</summary>
        public string State { get; set; }

        /// <summary>Docker URL.</summary>
        public string Url { get; set; }

        /// <summary>Whether swarm is active.</summary>
        public bool Swarm { get; set; }

        /// <summary>Driver used (hyperv, virtualbox, etc.).</summary>
        public string Driver { get; set; }

        /// <summary>Docker version on the machine.</summary>
        public string DockerVersion { get; set; }

        /// <summary>Active status.</summary>
        public bool Active { get; set; }

        /// <summary>Error message if any.</summary>
        public string Error { get; set; }
    }

    /// <summary>
    /// Detailed machine information.
    /// </summary>
    public class MachineDetails
    {
        /// <summary>Machine name.</summary>
        public string Name { get; set; }

        /// <summary>Driver used.</summary>
        public string Driver { get; set; }

        /// <summary>Driver configuration.</summary>
        public MachineDriverConfig DriverConfig { get; set; }

        /// <summary>Host configuration.</summary>
        public MachineHostConfig HostConfig { get; set; }

        /// <summary>Auth configuration.</summary>
        public MachineAuthConfig AuthConfig { get; set; }

        /// <summary>Raw JSON.</summary>
        public string RawJson { get; set; }
    }

    /// <summary>
    /// Machine driver configuration.
    /// </summary>
    public class MachineDriverConfig
    {
        /// <summary>IP address.</summary>
        public string IpAddress { get; set; }

        /// <summary>SSH user.</summary>
        public string SshUser { get; set; }

        /// <summary>SSH port.</summary>
        public int SshPort { get; set; }

        /// <summary>SSH key path.</summary>
        public string SshKeyPath { get; set; }

        /// <summary>Memory in MB.</summary>
        public int Memory { get; set; }

        /// <summary>Disk size in MB.</summary>
        public int DiskSize { get; set; }

        /// <summary>Number of CPUs.</summary>
        public int CpuCount { get; set; }

        /// <summary>Machine storage path.</summary>
        public string StorePath { get; set; }
    }

    /// <summary>
    /// Machine host configuration.
    /// </summary>
    public class MachineHostConfig
    {
        /// <summary>URL to Docker daemon.</summary>
        public string Url { get; set; }

        /// <summary>CA cert path.</summary>
        public string CaCertPath { get; set; }

        /// <summary>Server cert path.</summary>
        public string ServerCertPath { get; set; }

        /// <summary>Server key path.</summary>
        public string ServerKeyPath { get; set; }

        /// <summary>Storage path.</summary>
        public string StorePath { get; set; }
    }

    /// <summary>
    /// Machine authentication configuration.
    /// </summary>
    public class MachineAuthConfig
    {
        /// <summary>CA cert path.</summary>
        public string CaCertPath { get; set; }

        /// <summary>Client cert path.</summary>
        public string ClientCertPath { get; set; }

        /// <summary>Client key path.</summary>
        public string ClientKeyPath { get; set; }

        /// <summary>Certificate directory.</summary>
        public string CertDir { get; set; }
    }

    #endregion

    #region Config Types

    /// <summary>
    /// Configuration for creating a machine.
    /// </summary>
    public class MachineCreateConfig
    {
        /// <summary>Machine name.</summary>
        public string Name { get; set; }

        /// <summary>Driver to use (hyperv, virtualbox, amazonec2, etc.).</summary>
        public string Driver { get; set; }

        /// <summary>Memory in MB.</summary>
        public int? Memory { get; set; }

        /// <summary>Disk size in MB.</summary>
        public int? DiskSize { get; set; }

        /// <summary>Number of CPUs.</summary>
        public int? CpuCount { get; set; }

        /// <summary>Additional driver-specific options.</summary>
        public Dictionary<string, string> DriverOptions { get; set; } = new Dictionary<string, string>();

        /// <summary>Engine options.</summary>
        public MachineEngineOptions EngineOptions { get; set; }

        /// <summary>Swarm options.</summary>
        public MachineSwarmOptions SwarmOptions { get; set; }
    }

    /// <summary>
    /// Docker Engine options for machine creation.
    /// </summary>
    public class MachineEngineOptions
    {
        /// <summary>Storage driver.</summary>
        public string StorageDriver { get; set; }

        /// <summary>Insecure registries.</summary>
        public List<string> InsecureRegistries { get; set; } = new List<string>();

        /// <summary>Registry mirrors.</summary>
        public List<string> RegistryMirrors { get; set; } = new List<string>();

        /// <summary>Labels.</summary>
        public Dictionary<string, string> Labels { get; set; } = new Dictionary<string, string>();

        /// <summary>Environment variables.</summary>
        public Dictionary<string, string> Environment { get; set; } = new Dictionary<string, string>();

        /// <summary>Install URL for Docker.</summary>
        public string InstallUrl { get; set; }
    }

    /// <summary>
    /// Swarm options for machine creation.
    /// </summary>
    public class MachineSwarmOptions
    {
        /// <summary>Join an existing swarm.</summary>
        public bool IsSwarm { get; set; }

        /// <summary>Configure as swarm master.</summary>
        public bool Master { get; set; }

        /// <summary>Swarm discovery URL.</summary>
        public string Discovery { get; set; }

        /// <summary>Swarm strategy.</summary>
        public string Strategy { get; set; }

        /// <summary>Swarm host address.</summary>
        public string Host { get; set; }

        /// <summary>Swarm address to advertise.</summary>
        public string Address { get; set; }
    }

    #endregion
}

