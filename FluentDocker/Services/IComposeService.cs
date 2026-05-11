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
  }
}

