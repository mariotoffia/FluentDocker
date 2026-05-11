using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Drivers
{
  /// <summary>
  /// Service operations: logs, scale, rollback, and task listing.
  /// </summary>
  public partial interface IServiceDriver
  {
    #region Rollback Operations

    /// <summary>
    /// Rolls back a service to its previous version.
    /// </summary>
    /// <param name="context">Driver context</param>
    /// <param name="serviceId">Service ID or name</param>
    /// <param name="detach">Exit immediately instead of waiting for service to converge</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task<CommandResponse<Unit>> RollbackAsync(
        DriverContext context,
        string serviceId,
        bool detach = false,
        CancellationToken cancellationToken = default);

    #endregion

    #region Task Operations

    /// <summary>
    /// Lists tasks of a service.
    /// </summary>
    /// <param name="context">Driver context</param>
    /// <param name="serviceId">Service ID or name</param>
    /// <param name="filter">Optional filter parameters</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of tasks</returns>
    Task<CommandResponse<IList<ServiceTask>>> GetTasksAsync(
        DriverContext context,
        string serviceId,
        ServiceTaskFilter filter = null,
        CancellationToken cancellationToken = default);

    #endregion

    #region Log Operations

    /// <summary>
    /// Gets service logs.
    /// </summary>
    /// <param name="context">Driver context</param>
    /// <param name="serviceId">Service ID or name</param>
    /// <param name="config">Logs configuration</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Service logs</returns>
    Task<CommandResponse<string>> GetLogsAsync(
        DriverContext context,
        string serviceId,
        ServiceLogsConfig config = null,
        CancellationToken cancellationToken = default);

    #endregion

    #region Scale Operations

    /// <summary>
    /// Scales services to specified number of replicas.
    /// </summary>
    /// <param name="context">Driver context</param>
    /// <param name="serviceReplicas">Dictionary of service names to replica counts</param>
    /// <param name="detach">Exit immediately instead of waiting for service to converge</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task<CommandResponse<Unit>> ScaleAsync(
        DriverContext context,
        Dictionary<string, int> serviceReplicas,
        bool detach = false,
        CancellationToken cancellationToken = default);

    #endregion
  }

  #region Operation Info Types

  /// <summary>
  /// Represents a service task.
  /// </summary>
  public class ServiceTask
  {
    /// <summary>Task ID.</summary>
    public string Id { get; set; }

    /// <summary>Task name.</summary>
    public string Name { get; set; }

    /// <summary>Image used.</summary>
    public string Image { get; set; }

    /// <summary>Node the task is running on.</summary>
    public string Node { get; set; }

    /// <summary>Desired state.</summary>
    public string DesiredState { get; set; }

    /// <summary>Current state.</summary>
    public string CurrentState { get; set; }

    /// <summary>Error message if any.</summary>
    public string Error { get; set; }

    /// <summary>Ports exposed.</summary>
    public string Ports { get; set; }
  }

  #endregion

  #region Operation Config Types

  /// <summary>
  /// Filter for listing service tasks.
  /// </summary>
  public class ServiceTaskFilter
  {
    /// <summary>Filter by task ID.</summary>
    public string Id { get; set; }

    /// <summary>Filter by task name.</summary>
    public string Name { get; set; }

    /// <summary>Filter by node.</summary>
    public string Node { get; set; }

    /// <summary>Filter by desired state.</summary>
    public string DesiredState { get; set; }

    /// <summary>Don't truncate output.</summary>
    public bool NoTrunc { get; set; }

    /// <summary>Don't resolve IDs to names.</summary>
    public bool NoResolve { get; set; }

    /// <summary>Only display task IDs.</summary>
    public bool Quiet { get; set; }

    /// <summary>Output format.</summary>
    public string Format { get; set; }
  }

  /// <summary>
  /// Configuration for service logs.
  /// </summary>
  public class ServiceLogsConfig
  {
    /// <summary>Show extra details.</summary>
    public bool Details { get; set; }

    /// <summary>Follow log output.</summary>
    public bool Follow { get; set; }

    /// <summary>Show logs since timestamp.</summary>
    public string Since { get; set; }

    /// <summary>Number of lines to show from end.</summary>
    public int? Tail { get; set; }

    /// <summary>Show timestamps.</summary>
    public bool Timestamps { get; set; }

    /// <summary>Don't include task IDs.</summary>
    public bool NoTaskIds { get; set; }

    /// <summary>Don't truncate output.</summary>
    public bool NoTrunc { get; set; }

    /// <summary>Don't neatly format logs.</summary>
    public bool Raw { get; set; }

    /// <summary>Don't resolve IDs to names.</summary>
    public bool NoResolve { get; set; }
  }

  #endregion
}
