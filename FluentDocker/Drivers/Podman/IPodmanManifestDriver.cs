using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Drivers.Podman
{
    /// <summary>
    /// Driver for Podman manifest (multi-arch image) operations.
    /// Supports creating manifest lists, adding platform-specific images,
    /// inspecting, annotating, pushing, and removing manifests.
    /// </summary>
    public interface IPodmanManifestDriver
    {
        /// <summary>
        /// Creates a new manifest list.
        /// </summary>
        Task<CommandResponse<string>> CreateAsync(
            DriverContext context, ManifestCreateConfig config,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Adds an image to a manifest list.
        /// </summary>
        Task<CommandResponse<string>> AddAsync(
            DriverContext context, ManifestAddConfig config,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Pushes a manifest list to a registry.
        /// </summary>
        Task<CommandResponse<Unit>> PushAsync(
            DriverContext context, ManifestPushConfig config,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Inspects a manifest list, returning its structure.
        /// </summary>
        Task<CommandResponse<ManifestInspectResult>> InspectAsync(
            DriverContext context, string listName,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Annotates an entry in a manifest list with platform metadata.
        /// </summary>
        Task<CommandResponse<Unit>> AnnotateAsync(
            DriverContext context, ManifestAnnotateConfig config,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Removes a manifest list from local storage.
        /// </summary>
        Task<CommandResponse<Unit>> RemoveAsync(
            DriverContext context, string listName,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Checks whether a manifest list exists in local storage.
        /// </summary>
        Task<CommandResponse<bool>> ExistsAsync(
            DriverContext context, string listName,
            CancellationToken cancellationToken = default);
    }

    #region Configuration Models

    /// <summary>
    /// Configuration for creating a manifest list.
    /// </summary>
    public class ManifestCreateConfig
    {
        /// <summary>Name/tag for the manifest list (required).</summary>
        public string Name { get; set; }

        /// <summary>Optional images to include during creation.</summary>
        public List<string> Images { get; set; } = new List<string>();

        /// <summary>When true, include all contents from nested manifest lists.</summary>
        public bool All { get; set; }

        /// <summary>When true, amend an existing manifest list instead of failing.</summary>
        public bool Amend { get; set; }

        /// <summary>Optional annotations to set on the manifest list.</summary>
        public Dictionary<string, string> Annotations { get; set; } = new Dictionary<string, string>();
    }

    /// <summary>
    /// Configuration for adding an image to a manifest list.
    /// </summary>
    public class ManifestAddConfig
    {
        /// <summary>Name of the manifest list (required).</summary>
        public string ListName { get; set; }

        /// <summary>Image to add (required).</summary>
        public string Image { get; set; }

        /// <summary>Override the architecture for the image entry.</summary>
        public string Arch { get; set; }

        /// <summary>Override the OS for the image entry.</summary>
        public string Os { get; set; }

        /// <summary>Override the variant for the image entry.</summary>
        public string Variant { get; set; }

        /// <summary>Override the OS version for the image entry.</summary>
        public string OsVersion { get; set; }

        /// <summary>Features required for the image entry.</summary>
        public List<string> Features { get; set; } = new List<string>();

        /// <summary>Include all contents from a source manifest list.</summary>
        public bool All { get; set; }

        /// <summary>Optional annotations for the entry.</summary>
        public Dictionary<string, string> Annotations { get; set; } = new Dictionary<string, string>();
    }

    /// <summary>
    /// Configuration for pushing a manifest list to a registry.
    /// </summary>
    public class ManifestPushConfig
    {
        /// <summary>Name of the manifest list to push (required).</summary>
        public string ListName { get; set; }

        /// <summary>Destination registry/repository (required).</summary>
        public string Destination { get; set; }

        /// <summary>Push all referenced images (default true).</summary>
        public bool All { get; set; } = true;

        /// <summary>Remove the local manifest list after a successful push.</summary>
        public bool Rm { get; set; }

        /// <summary>Manifest format: "oci" or "v2s2" (default: oci).</summary>
        public string Format { get; set; }

        /// <summary>Enable/disable TLS verification. Null uses the default.</summary>
        public bool? TlsVerify { get; set; }
    }

    /// <summary>
    /// Configuration for annotating an entry in a manifest list.
    /// </summary>
    public class ManifestAnnotateConfig
    {
        /// <summary>Name of the manifest list (required).</summary>
        public string ListName { get; set; }

        /// <summary>Image digest or name within the list (required).</summary>
        public string Image { get; set; }

        /// <summary>Override the architecture.</summary>
        public string Arch { get; set; }

        /// <summary>Override the OS.</summary>
        public string Os { get; set; }

        /// <summary>Override the variant.</summary>
        public string Variant { get; set; }

        /// <summary>Override the OS version.</summary>
        public string OsVersion { get; set; }

        /// <summary>OS features for the entry.</summary>
        public List<string> OsFeatures { get; set; } = new List<string>();

        /// <summary>Features for the entry.</summary>
        public List<string> Features { get; set; } = new List<string>();

        /// <summary>Annotations for the entry.</summary>
        public Dictionary<string, string> Annotations { get; set; } = new Dictionary<string, string>();

        /// <summary>When true, apply annotations to the index itself.</summary>
        public bool IndexAnnotation { get; set; }
    }

    #endregion

    #region Result Models

    /// <summary>
    /// Result of inspecting a manifest list.
    /// </summary>
    public class ManifestInspectResult
    {
        public int SchemaVersion { get; set; }
        public string MediaType { get; set; }
        public List<ManifestEntry> Manifests { get; set; } = new List<ManifestEntry>();
    }

    /// <summary>
    /// An individual entry in a manifest list.
    /// </summary>
    public class ManifestEntry
    {
        public string MediaType { get; set; }
        public long Size { get; set; }
        public string Digest { get; set; }
        public ManifestPlatform Platform { get; set; }
        public Dictionary<string, string> Annotations { get; set; } = new Dictionary<string, string>();
    }

    /// <summary>
    /// Platform information for a manifest entry.
    /// </summary>
    public class ManifestPlatform
    {
        public string Architecture { get; set; }
        public string Os { get; set; }
        public string Variant { get; set; }
        public string OsVersion { get; set; }
        public List<string> Features { get; set; } = new List<string>();
    }

    #endregion
}
