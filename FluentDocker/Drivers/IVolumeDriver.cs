using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Volumes;

namespace FluentDocker.Drivers
{
  /// <summary>
  /// Provides operations for managing Docker and Podman volumes, including creation,
  /// removal, listing, inspection, and pruning of unused volumes.
  /// </summary>
  public interface IVolumeDriver
  {
    /// <summary>
    /// Creates a new volume with the specified configuration.
    /// </summary>
    /// <param name="context">The driver context providing host and authentication details.</param>
    /// <param name="config">
    /// The volume creation configuration including name, driver, driver options, and labels.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// A <see cref="CommandResponse{T}"/> containing a <see cref="VolumeCreateResult"/>
    /// with the created volume's name, driver, and any warnings.
    /// </returns>
    Task<CommandResponse<VolumeCreateResult>> CreateAsync(
        DriverContext context,
        VolumeCreateConfig config,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a volume by name. The volume must not be in use by any container unless
    /// <paramref name="force"/> is set to <c>true</c>.
    /// </summary>
    /// <param name="context">The driver context providing host and authentication details.</param>
    /// <param name="volumeName">The name of the volume to remove.</param>
    /// <param name="force">
    /// When <c>true</c>, forces removal of the volume even if it is in use.
    /// Defaults to <c>false</c>.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// A <see cref="CommandResponse{T}"/> with <see cref="Unit"/> indicating success or failure.
    /// </returns>
    Task<CommandResponse<Unit>> RemoveAsync(
        DriverContext context,
        string volumeName,
        bool force = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists volumes, optionally filtered by name or labels.
    /// </summary>
    /// <param name="context">The driver context providing host and authentication details.</param>
    /// <param name="filter">
    /// Optional filter criteria to narrow results by name or labels. Pass <c>null</c> to list all volumes.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// A <see cref="CommandResponse{T}"/> containing a list of <see cref="Volume"/> objects
    /// matching the filter criteria.
    /// </returns>
    Task<CommandResponse<IList<Volume>>> ListAsync(
        DriverContext context,
        VolumeListFilter filter = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves detailed information about a specific volume, including its driver,
    /// mountpoint, labels, and creation date.
    /// </summary>
    /// <param name="context">The driver context providing host and authentication details.</param>
    /// <param name="volumeName">The name of the volume to inspect.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// A <see cref="CommandResponse{T}"/> containing a <see cref="Volume"/> with the
    /// volume's full details.
    /// </returns>
    Task<CommandResponse<Volume>> InspectAsync(
        DriverContext context,
        string volumeName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes all volumes that are not referenced by any container.
    /// </summary>
    /// <param name="context">The driver context providing host and authentication details.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// A <see cref="CommandResponse{T}"/> containing a <see cref="VolumePruneResult"/>
    /// with the names of deleted volumes and total space reclaimed.
    /// </returns>
    Task<CommandResponse<VolumePruneResult>> PruneAsync(
        DriverContext context,
        CancellationToken cancellationToken = default);
  }

  /// <summary>
  /// Configuration for creating a Docker or Podman volume.
  /// </summary>
  public class VolumeCreateConfig
  {
    /// <summary>
    /// The name to assign to the volume. When <c>null</c>, the engine generates a
    /// random name automatically.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// The volume driver to use (e.g., "local", "nfs"). Defaults to "local".
    /// </summary>
    public string Driver { get; set; } = "local";

    /// <summary>
    /// Driver-specific options as key-value pairs (e.g., mount type, device path).
    /// The available options depend on the selected <see cref="Driver"/>.
    /// </summary>
    public Dictionary<string, string> DriverOpts { get; set; } = new Dictionary<string, string>();

    /// <summary>
    /// User-defined labels to attach to the volume as key-value metadata.
    /// </summary>
    public Dictionary<string, string> Labels { get; set; } = new Dictionary<string, string>();
  }

  /// <summary>
  /// Contains the result of a volume creation operation, including the assigned name
  /// and driver information.
  /// </summary>
  public class VolumeCreateResult
  {
    /// <summary>
    /// The name of the created volume (either the requested name or an engine-generated one).
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// The driver used by the created volume.
    /// </summary>
    public string Driver { get; set; }

    /// <summary>
    /// Any warnings generated during volume creation (e.g., deprecated options).
    /// </summary>
    public List<string> Warnings { get; set; } = new List<string>();
  }

  /// <summary>
  /// Filter criteria for listing volumes. All specified filters are combined with AND logic.
  /// </summary>
  public class VolumeListFilter
  {
    /// <summary>
    /// Filters volumes by name. Supports partial matching depending on the driver implementation.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Filters volumes by labels. Only volumes that have all specified key-value pairs are returned.
    /// </summary>
    public Dictionary<string, string> Labels { get; set; } = new Dictionary<string, string>();
  }

  /// <summary>
  /// Contains the result of a volume prune operation, listing deleted volumes and
  /// reclaimed disk space.
  /// </summary>
  public class VolumePruneResult
  {
    /// <summary>
    /// The names of volumes that were deleted during the prune operation.
    /// </summary>
    public List<string> VolumesDeleted { get; set; } = new List<string>();

    /// <summary>
    /// The total amount of disk space reclaimed, in bytes.
    /// </summary>
    public long SpaceReclaimed { get; set; }
  }
}
