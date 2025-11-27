using System.Threading;
using System.Threading.Tasks;
using Ductus.FluentDocker.Model.Volumes;

namespace Ductus.FluentDocker.Services.V3
{
    /// <summary>
    /// v3.0.0 async volume service interface.
    /// </summary>
    public interface IVolumeServiceAsync : IServiceAsync
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

