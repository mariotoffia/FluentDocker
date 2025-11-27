using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Drivers
{
    /// <summary>
    /// System-level driver operations (info, version, ping, etc.).
    /// </summary>
    public interface ISystemDriver
    {
        /// <summary>
        /// Gets system information.
        /// </summary>
        Task<CommandResponse<SystemInfo>> GetInfoAsync(
            DriverContext context,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets version information.
        /// </summary>
        Task<CommandResponse<VersionInfo>> GetVersionAsync(
            DriverContext context,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Pings the daemon to check if it's responsive.
        /// </summary>
        Task<CommandResponse<Unit>> PingAsync(
            DriverContext context,
            CancellationToken cancellationToken = default);
    }

    public class SystemInfo
    {
        public string OperatingSystem { get; set; }
        public string Architecture { get; set; }
        public int Containers { get; set; }
        public int ContainersRunning { get; set; }
        public int Images { get; set; }
        public string ServerVersion { get; set; }
    }

    public class VersionInfo
    {
        public string Version { get; set; }
        public string ApiVersion { get; set; }
        public string GitCommit { get; set; }
        public string GoVersion { get; set; }
        public string Os { get; set; }
        public string Arch { get; set; }
    }
}
