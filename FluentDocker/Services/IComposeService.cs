using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers;

namespace FluentDocker.Services
{
  /// <summary>
  /// Async compose service interface.
  /// </summary>
  /// <remarks>
  /// Services created by <c>ConnectToExisting</c>, or by an up build that detects a pre-existing
  /// project, are borrowed handles: disposing them releases local resources only and does not run
  /// <c>docker compose down</c>. Services created by normal compose builds own the project and run
  /// <c>down</c> on dispose.
  /// </remarks>
  public interface IComposeService : IServiceAsync
  {
    /// <summary>
    /// Explicitly configured project name, or <c>null</c> when compose derived the name from
    /// the project directory (commands then identify the project via <see cref="ComposeFiles"/>).
    /// </summary>
    string? ProjectName { get; }

    /// <summary>
    /// Compose file paths.
    /// </summary>
    IReadOnlyList<string> ComposeFiles { get; }

    /// <summary>
    /// Gets whether this service is a borrowed handle to a pre-existing compose project.
    /// </summary>
    /// <remarks>
    /// Borrowed services do not run <c>docker compose down</c> when disposed; they release
    /// FluentDocker-local resources only.
    /// </remarks>
    bool IsBorrowed { get; }

    /// <summary>
    /// Lists all services in this compose project.
    /// </summary>
    Task<IList<ComposeServiceInfo>> ListServicesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets buffered logs from compose services.
    /// </summary>
    /// <remarks>
    /// <paramref name="follow"/> is rejected by the CLI driver because buffered follow blocks
    /// until services exit.
    /// </remarks>
    Task<string> GetLogsAsync(bool follow = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a command in a specific service.
    /// </summary>
    Task<string> ExecuteAsync(string service, string[] command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Scales a service to the specified number of instances.
    /// </summary>
    Task ScaleAsync(string service, int replicas, CancellationToken cancellationToken = default);

    /// <summary>
    /// Restarts the whole compose project (<c>docker compose restart</c>).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RestartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Restarts specific services in the compose project
    /// (<c>docker compose restart &lt;service&gt; …</c>).
    /// </summary>
    /// <param name="services">The services to restart. When null or empty, the whole project is restarted.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RestartAsync(IEnumerable<string>? services, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resumes paused services in the compose project.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task UnpauseAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Refreshes <see cref="IServiceAsync.State"/> by querying the live per-service status
    /// (<c>docker compose ps</c>). Useful after attaching to an existing project (see
    /// <c>ConnectToExisting</c>) so the aggregate state reflects what the daemon reports
    /// rather than the assumed default. The aggregate is resolved in this precedence order:
    /// <list type="bullet">
    ///   <item><c>Running</c> — any service is running;</item>
    ///   <item><c>Starting</c> — otherwise, any service is restarting;</item>
    ///   <item><c>Stopped</c> — otherwise, every service is stopped/exited/dead;</item>
    ///   <item><c>Paused</c> — otherwise, every service is paused;</item>
    ///   <item><c>Unknown</c> — otherwise (a mixed set of states, or no services exist).</item>
    /// </list>
    /// A project already <c>Removed</c> stays <c>Removed</c>; an empty <c>ps</c> result does not
    /// resurrect it.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RefreshStateAsync(CancellationToken cancellationToken = default);
  }
}
