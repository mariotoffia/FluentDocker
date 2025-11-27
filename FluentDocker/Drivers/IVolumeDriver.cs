using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Volumes;

namespace FluentDocker.Drivers
{
    /// <summary>
    /// Volume-specific driver operations.
    /// </summary>
    public interface IVolumeDriver
    {
        /// <summary>
        /// Creates a volume.
        /// </summary>
        Task<CommandResponse<VolumeCreateResult>> CreateAsync(
            DriverContext context,
            VolumeCreateConfig config,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Removes a volume.
        /// </summary>
        Task<CommandResponse<Unit>> RemoveAsync(
            DriverContext context,
            string volumeName,
            bool force = false,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Lists volumes.
        /// </summary>
        Task<CommandResponse<IList<Volume>>> ListAsync(
            DriverContext context,
            VolumeListFilter filter = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Inspects a volume.
        /// </summary>
        Task<CommandResponse<Volume>> InspectAsync(
            DriverContext context,
            string volumeName,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Prunes unused volumes.
        /// </summary>
        Task<CommandResponse<VolumePruneResult>> PruneAsync(
            DriverContext context,
            CancellationToken cancellationToken = default);
    }

    public class VolumeCreateConfig
    {
        public string Name { get; set; }
        public string Driver { get; set; } = "local";
        public Dictionary<string, string> DriverOpts { get; set; } = new Dictionary<string, string>();
        public Dictionary<string, string> Labels { get; set; } = new Dictionary<string, string>();
    }

    public class VolumeCreateResult
    {
        public string Name { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public class VolumeListFilter
    {
        public string Name { get; set; }
        public Dictionary<string, string> Labels { get; set; } = new Dictionary<string, string>();
    }

    public class VolumePruneResult
    {
        public List<string> VolumesDeleted { get; set; } = new List<string>();
        public long SpaceReclaimed { get; set; }
    }
}
