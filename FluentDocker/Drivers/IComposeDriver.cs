using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Drivers
{
    /// <summary>
    /// Compose-specific driver operations (docker-compose, podman-compose).
    /// </summary>
    public interface IComposeDriver
    {
        /// <summary>
        /// Starts all services defined in a compose file.
        /// </summary>
        Task<CommandResponse<ComposeUpResult>> UpAsync(
            DriverContext context,
            ComposeUpConfig config,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Stops and removes all services defined in a compose file.
        /// </summary>
        Task<CommandResponse<Unit>> DownAsync(
            DriverContext context,
            ComposeDownConfig config,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Starts existing compose services.
        /// </summary>
        Task<CommandResponse<Unit>> StartAsync(
            DriverContext context,
            string composeFile,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Stops running compose services.
        /// </summary>
        Task<CommandResponse<Unit>> StopAsync(
            DriverContext context,
            string composeFile,
            int? timeout = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Lists services in a compose project.
        /// </summary>
        Task<CommandResponse<IList<ComposeService>>> ListAsync(
            DriverContext context,
            string composeFile,
            string projectName = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets logs from compose services.
        /// </summary>
        Task<CommandResponse<string>> GetLogsAsync(
            DriverContext context,
            string composeFile,
            bool follow = false,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Executes a command in a compose service.
        /// </summary>
        Task<CommandResponse<string>> ExecuteAsync(
            DriverContext context,
            string composeFile,
            string service,
            string[] command,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Configuration for compose up operation.
    /// </summary>
    public class ComposeUpConfig
    {
        /// <summary>
        /// Path to compose file(s).
        /// </summary>
        public List<string> ComposeFiles { get; set; } = new List<string>();

        /// <summary>
        /// Project name.
        /// </summary>
        public string ProjectName { get; set; }

        /// <summary>
        /// Build images before starting.
        /// </summary>
        public bool Build { get; set; }

        /// <summary>
        /// Force recreate containers.
        /// </summary>
        public bool ForceRecreate { get; set; }

        /// <summary>
        /// Detached mode.
        /// </summary>
        public bool Detached { get; set; } = true;

        /// <summary>
        /// Remove orphan containers.
        /// </summary>
        public bool RemoveOrphans { get; set; }

        /// <summary>
        /// Environment variables.
        /// </summary>
        public Dictionary<string, string> Environment { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Specific services to start.
        /// </summary>
        public List<string> Services { get; set; } = new List<string>();

        /// <summary>
        /// Don't start linked services.
        /// </summary>
        public bool NoDeps { get; set; }

        /// <summary>
        /// Shutdown timeout in seconds.
        /// </summary>
        public int? Timeout { get; set; }
    }

    /// <summary>
    /// Configuration for compose down operation.
    /// </summary>
    public class ComposeDownConfig
    {
        /// <summary>
        /// Path to compose file(s).
        /// </summary>
        public List<string> ComposeFiles { get; set; } = new List<string>();

        /// <summary>
        /// Project name.
        /// </summary>
        public string ProjectName { get; set; }

        /// <summary>
        /// Remove volumes.
        /// </summary>
        public bool RemoveVolumes { get; set; }

        /// <summary>
        /// Remove images (all, local).
        /// </summary>
        public string RemoveImages { get; set; }

        /// <summary>
        /// Timeout in seconds.
        /// </summary>
        public int? Timeout { get; set; }

        /// <summary>
        /// Remove orphaned containers.
        /// </summary>
        public bool RemoveOrphans { get; set; }
    }

    /// <summary>
    /// Result of a compose up operation.
    /// </summary>
    public class ComposeUpResult
    {
        /// <summary>
        /// List of started services.
        /// </summary>
        public List<string> Services { get; set; } = new List<string>();

        /// <summary>
        /// Project name.
        /// </summary>
        public string ProjectName { get; set; }

        /// <summary>
        /// Warnings from the operation.
        /// </summary>
        public List<string> Warnings { get; set; } = new List<string>();
    }

    /// <summary>
    /// Represents a compose service.
    /// </summary>
    public class ComposeService
    {
        /// <summary>
        /// Service name.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Current state.
        /// </summary>
        public string State { get; set; }

        /// <summary>
        /// Container ID (if running).
        /// </summary>
        public string ContainerId { get; set; }

        /// <summary>
        /// Image being used.
        /// </summary>
        public string Image { get; set; }

        /// <summary>
        /// Port mappings.
        /// </summary>
        public List<string> Ports { get; set; } = new List<string>();
    }
}
