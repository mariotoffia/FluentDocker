using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Drivers
{
    /// <summary>
    /// Compose-specific driver operations (docker-compose, podman-compose).
    /// Supported by: Docker (V1 and V2), Podman (partial)
    /// </summary>
    public interface IComposeDriver
    {
        #region Lifecycle Operations

        /// <summary>
        /// Creates and starts all services defined in a compose file.
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
        /// Starts existing compose services (previously created).
        /// </summary>
        Task<CommandResponse<Unit>> StartAsync(
            DriverContext context,
            ComposeFileConfig config,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Stops running compose services.
        /// </summary>
        Task<CommandResponse<Unit>> StopAsync(
            DriverContext context,
            ComposeStopConfig config,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Restarts compose services.
        /// </summary>
        Task<CommandResponse<Unit>> RestartAsync(
            DriverContext context,
            ComposeRestartConfig config,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Pauses running compose services.
        /// </summary>
        Task<CommandResponse<Unit>> PauseAsync(
            DriverContext context,
            ComposeFileConfig config,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Unpauses paused compose services.
        /// </summary>
        Task<CommandResponse<Unit>> UnpauseAsync(
            DriverContext context,
            ComposeFileConfig config,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Kills running compose services with a signal.
        /// </summary>
        Task<CommandResponse<Unit>> KillAsync(
            DriverContext context,
            ComposeKillConfig config,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Removes stopped service containers.
        /// </summary>
        Task<CommandResponse<Unit>> RemoveAsync(
            DriverContext context,
            ComposeRemoveConfig config,
            CancellationToken cancellationToken = default);

        #endregion

        #region Information Operations

        /// <summary>
        /// Lists services in a compose project.
        /// </summary>
        Task<CommandResponse<IList<ComposeServiceInfo>>> ListAsync(
            DriverContext context,
            ComposeListConfig config,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets logs from compose services.
        /// </summary>
        Task<CommandResponse<string>> GetLogsAsync(
            DriverContext context,
            ComposeLogsConfig config,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets running processes in compose services.
        /// </summary>
        Task<CommandResponse<IList<ComposeProcesses>>> TopAsync(
            DriverContext context,
            ComposeFileConfig config,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Validates and views the compose file.
        /// </summary>
        Task<CommandResponse<string>> ConfigAsync(
            DriverContext context,
            ComposeConfigConfig config,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Lists images used by the created containers.
        /// </summary>
        Task<CommandResponse<IList<ComposeImage>>> ImagesAsync(
            DriverContext context,
            ComposeFileConfig config,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the public port for a port binding.
        /// </summary>
        Task<CommandResponse<string>> PortAsync(
            DriverContext context,
            ComposePortConfig config,
            CancellationToken cancellationToken = default);

        #endregion

        #region Build/Pull Operations

        /// <summary>
        /// Builds or rebuilds services.
        /// </summary>
        Task<CommandResponse<Unit>> BuildAsync(
            DriverContext context,
            ComposeBuildConfig config,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Pulls images for services defined in compose file.
        /// </summary>
        Task<CommandResponse<Unit>> PullAsync(
            DriverContext context,
            ComposePullConfig config,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Pushes images for services.
        /// </summary>
        Task<CommandResponse<Unit>> PushAsync(
            DriverContext context,
            ComposeFileConfig config,
            CancellationToken cancellationToken = default);

        #endregion

        #region Execution Operations

        /// <summary>
        /// Executes a command in a compose service container.
        /// </summary>
        Task<CommandResponse<string>> ExecuteAsync(
            DriverContext context,
            ComposeExecConfig config,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Runs a one-off command in a service container.
        /// </summary>
        Task<CommandResponse<string>> RunAsync(
            DriverContext context,
            ComposeRunConfig config,
            CancellationToken cancellationToken = default);

        #endregion

        #region Scale/Copy Operations

        /// <summary>
        /// Scales services to specified number of instances.
        /// </summary>
        Task<CommandResponse<Unit>> ScaleAsync(
            DriverContext context,
            ComposeScaleConfig config,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Copies files/folders between a service container and local filesystem.
        /// </summary>
        Task<CommandResponse<Unit>> CopyAsync(
            DriverContext context,
            ComposeCopyConfig config,
            CancellationToken cancellationToken = default);

        #endregion

        #region Create Operations

        /// <summary>
        /// Creates containers for services without starting them.
        /// </summary>
        Task<CommandResponse<Unit>> CreateAsync(
            DriverContext context,
            ComposeCreateConfig config,
            CancellationToken cancellationToken = default);

        #endregion
    }
}
