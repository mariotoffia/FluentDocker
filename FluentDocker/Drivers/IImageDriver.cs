using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Images;

namespace FluentDocker.Drivers
{
  /// <summary>
  /// Image-specific driver operations.
  /// Supported by: Docker, Podman
  /// Not supported by: Kubernetes (images are managed externally)
  /// </summary>
  public interface IImageDriver
  {
    #region Pull/Push Operations

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
    /// Pushes an image to a registry.
    /// </summary>
    /// <param name="context">Driver context</param>
    /// <param name="image">Image name with tag</param>
    /// <param name="progress">Progress reporter</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task<CommandResponse<Unit>> PushAsync(
        DriverContext context,
        string image,
        IProgress<ImagePushProgress> progress = null,
        CancellationToken cancellationToken = default);

    #endregion

    #region Build Operations

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

    #endregion

    #region List/Inspect Operations

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
    /// Shows image layer history.
    /// </summary>
    /// <param name="context">Driver context</param>
    /// <param name="imageId">Image ID or name</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of image layers</returns>
    Task<CommandResponse<IList<ImageLayer>>> HistoryAsync(
        DriverContext context,
        string imageId,
        CancellationToken cancellationToken = default);

    #endregion

    #region Tag/Remove Operations

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

    /// <summary>
    /// Removes an image.
    /// </summary>
    /// <param name="context">Driver context</param>
    /// <param name="imageId">Image ID or name</param>
    /// <param name="force">Force removal</param>
    /// <param name="noPrune">Don't remove untagged parents</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task<CommandResponse<ImageRemoveResult>> RemoveAsync(
        DriverContext context,
        string imageId,
        bool force = false,
        bool noPrune = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes unused images.
    /// </summary>
    /// <param name="context">Driver context</param>
    /// <param name="all">Remove all unused images, not just dangling</param>
    /// <param name="filter">Filter to provide</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task<CommandResponse<ImagePruneResult>> PruneAsync(
        DriverContext context,
        bool all = false,
        Dictionary<string, string> filter = null,
        CancellationToken cancellationToken = default);

    #endregion

    #region Save/Load/Import Operations

    /// <summary>
    /// Saves images to a tar archive.
    /// </summary>
    /// <param name="context">Driver context</param>
    /// <param name="images">Images to save</param>
    /// <param name="outputPath">Output file path</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task<CommandResponse<Unit>> SaveAsync(
        DriverContext context,
        string[] images,
        string outputPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads images from a tar archive.
    /// </summary>
    /// <param name="context">Driver context</param>
    /// <param name="inputPath">Input file path</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of loaded image names</returns>
    Task<CommandResponse<IList<string>>> LoadAsync(
        DriverContext context,
        string inputPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Imports a container as an image.
    /// </summary>
    /// <param name="context">Driver context</param>
    /// <param name="source">Source file path or URL</param>
    /// <param name="repository">Target repository name</param>
    /// <param name="tag">Target tag</param>
    /// <param name="message">Commit message</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Imported image ID</returns>
    Task<CommandResponse<string>> ImportAsync(
        DriverContext context,
        string source,
        string repository = null,
        string tag = null,
        string message = null,
        CancellationToken cancellationToken = default);

    #endregion
  }

  #region Progress Types

  /// <summary>
  /// Progress information for image pull operations.
  /// </summary>
  public class ImagePullProgress
  {
    /// <summary>Status message for the current pull step (e.g. "Downloading", "Pull complete").</summary>
    public string Status { get; set; }

    /// <summary>Human-readable progress bar (e.g. "[=====>   ] 5.12MB/10.24MB").</summary>
    public string Progress { get; set; }

    /// <summary>Layer digest being processed. Null for non-layer-specific messages.</summary>
    public string Id { get; set; }

    /// <summary>Bytes downloaded so far for the current layer.</summary>
    public long Current { get; set; }

    /// <summary>Total bytes to download for the current layer.</summary>
    public long Total { get; set; }
  }

  /// <summary>
  /// Progress information for image push operations.
  /// </summary>
  public class ImagePushProgress
  {
    /// <summary>Status message for the current push step (e.g. "Pushing", "Layer already exists").</summary>
    public string Status { get; set; }

    /// <summary>Human-readable progress bar for the upload.</summary>
    public string Progress { get; set; }

    /// <summary>Layer digest being uploaded. Null for non-layer-specific messages.</summary>
    public string Id { get; set; }

    /// <summary>Bytes uploaded so far for the current layer.</summary>
    public long Current { get; set; }

    /// <summary>Total bytes to upload for the current layer.</summary>
    public long Total { get; set; }
  }

  /// <summary>
  /// Progress information for image build operations.
  /// </summary>
  public class ImageBuildProgress
  {
    /// <summary>Build output stream text (e.g. "Step 1/5 : FROM alpine:latest").</summary>
    public string Stream { get; set; }

    /// <summary>Status message for the build step.</summary>
    public string Status { get; set; }

    /// <summary>Image ID associated with the build event. Null for regular stream output.</summary>
    public string Id { get; set; }

    /// <summary>Error message if the build step failed. Null on success.</summary>
    public string Error { get; set; }
  }

  #endregion

  #region Config Types

  /// <summary>
  /// Configuration for building an image.
  /// </summary>
  public class ImageBuildConfig
  {
    /// <summary>Path to Dockerfile or build context.</summary>
    public string BuildContext { get; set; }

    /// <summary>Dockerfile name (if not "Dockerfile").</summary>
    public string DockerfileName { get; set; }

    /// <summary>Tags to apply to the built image.</summary>
    public List<string> Tags { get; set; } = [];

    /// <summary>Build arguments.</summary>
    public Dictionary<string, string> BuildArgs { get; set; } = [];

    /// <summary>Target build stage (for multi-stage builds).</summary>
    public string Target { get; set; }

    /// <summary>Labels to apply.</summary>
    public Dictionary<string, string> Labels { get; set; } = [];

    /// <summary>Don't use cache when building.</summary>
    public bool NoCache { get; set; }

    /// <summary>Always attempt to pull newer versions of base images.</summary>
    public bool Pull { get; set; }

    /// <summary>Remove intermediate containers after build.</summary>
    public bool Rm { get; set; } = true;

    /// <summary>Always remove intermediate containers.</summary>
    public bool ForceRm { get; set; }

    /// <summary>Squash newly built layers into a single layer.</summary>
    public bool Squash { get; set; }

    /// <summary>Platform to build for.</summary>
    public string Platform { get; set; }

    /// <summary>Network mode during build.</summary>
    public string NetworkMode { get; set; }

    /// <summary>Memory limit for build.</summary>
    public long? Memory { get; set; }

    /// <summary>CPU quota for build.</summary>
    public long? CpuQuota { get; set; }
  }

  /// <summary>
  /// Filter parameters for listing images.
  /// </summary>
  public class ImageListFilter
  {
    /// <summary>Include all images (including intermediates).</summary>
    public bool All { get; set; }

    /// <summary>Filter by reference (name:tag).</summary>
    public string Reference { get; set; }

    /// <summary>Show dangling images only.</summary>
    public bool? Dangling { get; set; }

    /// <summary>Filter by label.</summary>
    public Dictionary<string, string> Labels { get; set; } = [];

    /// <summary>Filter by before image.</summary>
    public string Before { get; set; }

    /// <summary>Filter by since image.</summary>
    public string Since { get; set; }
  }

  #endregion

  #region Result Types

  /// <summary>
  /// Result of an image build operation.
  /// </summary>
  public class ImageBuildResult
  {
    /// <summary>Built image ID.</summary>
    public string ImageId { get; set; }

    /// <summary>Build warnings.</summary>
    public List<string> Warnings { get; set; } = [];

    /// <summary>Build output.</summary>
    public List<string> Output { get; set; } = [];
  }

  /// <summary>
  /// Result of an image remove operation.
  /// </summary>
  public class ImageRemoveResult
  {
    /// <summary>Deleted image IDs.</summary>
    public List<string> Deleted { get; set; } = [];

    /// <summary>Untagged image references.</summary>
    public List<string> Untagged { get; set; } = [];
  }

  /// <summary>
  /// Result of an image prune operation.
  /// </summary>
  public class ImagePruneResult
  {
    /// <summary>Deleted image IDs.</summary>
    public List<string> ImagesDeleted { get; set; } = [];

    /// <summary>Space reclaimed in bytes.</summary>
    public long SpaceReclaimed { get; set; }
  }

  /// <summary>
  /// Represents an image layer.
  /// </summary>
  public class ImageLayer
  {
    /// <summary>Layer ID.</summary>
    public string Id { get; set; }

    /// <summary>Created by command.</summary>
    public string CreatedBy { get; set; }

    /// <summary>Creation time.</summary>
    public DateTime Created { get; set; }

    /// <summary>Layer size in bytes.</summary>
    public long Size { get; set; }

    /// <summary>Comment.</summary>
    public string Comment { get; set; }

    /// <summary>Tags associated with this layer.</summary>
    public List<string> Tags { get; set; } = [];
  }

  /// <summary>
  /// Represents an image.
  /// </summary>
  public class Image
  {
    /// <summary>Image ID.</summary>
    public string Id { get; set; }

    /// <summary>Parent image ID.</summary>
    public string ParentId { get; set; }

    /// <summary>Repository tags.</summary>
    public List<string> RepoTags { get; set; } = [];

    /// <summary>Repository digests.</summary>
    public List<string> RepoDigests { get; set; } = [];

    /// <summary>Creation time.</summary>
    public DateTime Created { get; set; }

    /// <summary>Image size in bytes.</summary>
    public long Size { get; set; }

    /// <summary>Virtual size in bytes.</summary>
    public long VirtualSize { get; set; }

    /// <summary>Image labels.</summary>
    public Dictionary<string, string> Labels { get; set; } = [];

    /// <summary>Number of containers using this image.</summary>
    public int Containers { get; set; }

    /// <summary>Architecture.</summary>
    public string Architecture { get; set; }

    /// <summary>Operating system.</summary>
    public string Os { get; set; }
  }

  #endregion
}
