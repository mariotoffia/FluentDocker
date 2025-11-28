using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Model.Volumes;

namespace FluentDocker.Services
{
    /// <summary>
    /// Async volume service interface.
    /// </summary>
    public interface IVolumeService : IServiceAsync
    {
        /// <summary>
        /// Volume name.
        /// </summary>
        string VolumeName { get; }

        /// <summary>
        /// Volume driver.
        /// </summary>
        string Driver { get; }

        /// <summary>
        /// Inspects the volume to get detailed information.
        /// </summary>
        Task<Volume> InspectAsync(CancellationToken cancellationToken = default);
    }
}

