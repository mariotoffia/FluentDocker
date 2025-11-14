using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ductus.FluentDocker.Model.Drivers;
using Ductus.FluentDocker.Model.Images;

namespace Ductus.FluentDocker.Drivers
{
    /// <summary>
    /// Image-specific driver operations.
    /// </summary>
    public interface IImageDriver
    {
        /// <summary>
        /// Pulls an image from a registry.
        /// </summary>
        /// <param name="context">Driver context</param>
        /// <param name="image">Image name</param>
        /// <param name="tag">Image tag (default: latest)</param>
        /// <param name="progress">Progress reporter</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task<CommandResponse<Unit>> PullAsync(
            DriverContext context,
            string image,
            string tag = "latest",
            IProgress<ImagePullProgress> progress = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Removes an image.
        /// </summary>
        /// <param name="context">Driver context</param>
        /// <param name="imageId">Image ID or name</param>
        /// <param name="force">Force removal</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task<CommandResponse<Unit>> RemoveAsync(
            DriverContext context,
            string imageId,
            bool force = false,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Builds an image from a Dockerfile.
        /// </summary>
        /// <param name="context">Driver context</param>
        /// <param name="config">Build configuration</param>
        /// <param name="progress">Progress reporter</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Build result with image ID</returns>
        Task<CommandResponse<ImageBuildResult>> BuildAsync(
            DriverContext context,
            ImageBuildConfig config,
            IProgress<ImageBuildProgress> progress = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Lists images.
        /// </summary>
        /// <param name="context">Driver context</param>
        /// <param name="filter">Optional filter parameters</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>List of images</returns>
        Task<CommandResponse<IList<Image>>> ListAsync(
            DriverContext context,
            ImageListFilter filter = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Inspects an image.
        /// </summary>
        /// <param name="context">Driver context</param>
        /// <param name="imageId">Image ID or name</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Detailed image information</returns>
        Task<CommandResponse<Image>> InspectAsync(
            DriverContext context,
            string imageId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Tags an image.
        /// </summary>
        /// <param name="context">Driver context</param>
        /// <param name="imageId">Source image ID or name</param>
        /// <param name="repository">Target repository</param>
        /// <param name="tag">Target tag</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task<CommandResponse<Unit>> TagAsync(
            DriverContext context,
            string imageId,
            string repository,
            string tag,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Progress information for image pull operations.
    /// </summary>
    public class ImagePullProgress
    {
        public string Status { get; set; }
        public string Progress { get; set; }
        public long Current { get; set; }
        public long Total { get; set; }
    }

    /// <summary>
    /// Progress information for image build operations.
    /// </summary>
    public class ImageBuildProgress
    {
        public string Stream { get; set; }
        public string Status { get; set; }
    }

    /// <summary>
    /// Configuration for building an image.
    /// </summary>
    public class ImageBuildConfig
    {
        /// <summary>
        /// Path to Dockerfile or build context.
        /// </summary>
        public string BuildContext { get; set; }

        /// <summary>
        /// Dockerfile name (if not "Dockerfile").
        /// </summary>
        public string DockerfileName { get; set; }

        /// <summary>
        /// Tags to apply to the built image.
        /// </summary>
        public List<string> Tags { get; set; } = new List<string>();

        /// <summary>
        /// Build arguments.
        /// </summary>
        public Dictionary<string, string> BuildArgs { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Target build stage (for multi-stage builds).
        /// </summary>
        public string Target { get; set; }

        /// <summary>
        /// Labels to apply.
        /// </summary>
        public Dictionary<string, string> Labels { get; set; } = new Dictionary<string, string>();
    }

    /// <summary>
    /// Result of an image build operation.
    /// </summary>
    public class ImageBuildResult
    {
        /// <summary>
        /// Built image ID.
        /// </summary>
        public string ImageId { get; set; }

        /// <summary>
        /// Build warnings.
        /// </summary>
        public List<string> Warnings { get; set; } = new List<string>();
    }

    /// <summary>
    /// Filter parameters for listing images.
    /// </summary>
    public class ImageListFilter
    {
        /// <summary>
        /// Include all images (including intermediates).
        /// </summary>
        public bool All { get; set; }

        /// <summary>
        /// Filter by reference (name:tag).
        /// </summary>
        public string Reference { get; set; }

        /// <summary>
        /// Filter by label.
        /// </summary>
        public Dictionary<string, string> Labels { get; set; } = new Dictionary<string, string>();
    }
}
