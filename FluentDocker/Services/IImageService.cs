using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers;

namespace FluentDocker.Services
{
    /// <summary>
    /// Async image service interface.
    /// </summary>
    public interface IImageService : IServiceAsync
    {
        /// <summary>
        /// Image ID.
        /// </summary>
        string Id { get; }

        /// <summary>
        /// Image tag.
        /// </summary>
        string Tag { get; }

        /// <summary>
        /// Full image name (repository:tag).
        /// </summary>
        string FullName { get; }

        /// <summary>
        /// Gets detailed image information asynchronously.
        /// </summary>
        Task<Image> InspectAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the image layer history asynchronously.
        /// </summary>
        Task<IList<ImageLayer>> GetHistoryAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Tags the image with a new repository and tag.
        /// </summary>
        /// <param name="repository">Target repository name.</param>
        /// <param name="tag">Target tag.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task TagAsync(string repository, string tag, CancellationToken cancellationToken = default);

        /// <summary>
        /// Pushes the image to a registry.
        /// </summary>
        /// <param name="progress">Optional progress reporter.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task PushAsync(IProgress<ImagePushProgress> progress = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Saves the image to a tar archive.
        /// </summary>
        /// <param name="outputPath">Path to save the tar archive.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task SaveAsync(string outputPath, CancellationToken cancellationToken = default);
    }
}

