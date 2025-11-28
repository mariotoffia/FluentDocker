using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Drivers
{
    /// <summary>
    /// Authentication driver for registry login/logout operations.
    /// Supported by: Docker, Podman, Kubernetes (partial - imagePullSecrets)
    /// </summary>
    public interface IAuthDriver
    {
        /// <summary>
        /// Logs in to a container registry.
        /// </summary>
        /// <param name="context">Driver context</param>
        /// <param name="config">Login configuration</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task<CommandResponse<Unit>> LoginAsync(
            DriverContext context,
            RegistryLoginConfig config,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Logs out from a container registry.
        /// </summary>
        /// <param name="context">Driver context</param>
        /// <param name="server">Registry server (null for Docker Hub)</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task<CommandResponse<Unit>> LogoutAsync(
            DriverContext context,
            string server = null,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Configuration for registry login.
    /// </summary>
    public class RegistryLoginConfig
    {
        /// <summary>
        /// Registry server URL (null for Docker Hub).
        /// </summary>
        public string Server { get; set; }

        /// <summary>
        /// Username for authentication.
        /// </summary>
        public string Username { get; set; }

        /// <summary>
        /// Password for authentication.
        /// </summary>
        public string Password { get; set; }

        /// <summary>
        /// Read password from stdin (more secure).
        /// </summary>
        public bool PasswordStdin { get; set; }

        /// <summary>
        /// Email address (deprecated in newer Docker versions).
        /// </summary>
        public string Email { get; set; }
    }
}

