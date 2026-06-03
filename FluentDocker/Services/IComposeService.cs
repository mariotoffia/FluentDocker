using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers;

namespace FluentDocker.Services
{
  /// <summary>
  /// Async compose service interface.
  /// </summary>
  public interface IComposeService : IServiceAsync
  {
    /// <summary>
    /// Project name.
    /// </summary>
    string ProjectName { get; }

    /// <summary>
    /// Compose file paths.
    /// </summary>
    IReadOnlyList<string> ComposeFiles { get; }

    /// <summary>
    /// Lists all services in this compose project.
    /// </summary>
    Task<IList<ComposeServiceInfo>> ListServicesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets logs from compose services.
    /// </summary>
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
    /// Refreshes <see cref="IServiceAsync.State"/> by querying the live per-service status
    /// (<c>docker compose ps</c>). Useful after attaching to an existing project (see
    /// <c>ConnectToExisting</c>) so the aggregate state reflects what the daemon reports
    /// rather than the assumed default. The state becomes <c>Running</c> if any service is
    /// running, <c>Stopped</c> if all are stopped/exited, or <c>Unknown</c> if no services exist.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RefreshStateAsync(CancellationToken cancellationToken = default);
  }
}

