using System;
using System.Collections.Generic;

namespace FluentDocker.Model.Images
{
    /// <summary>
    /// Represents a Docker image for v3.0.0 API.
    /// </summary>
    public class Image
    {
        /// <summary>
        /// Image ID (sha256:...).
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// Repository name (e.g., "nginx", "myregistry.com/myimage").
        /// </summary>
        public string Repository { get; set; }

        /// <summary>
        /// Image tags (e.g., "latest", "1.0.0").
        /// </summary>
        public List<string> Tags { get; set; } = new List<string>();

        /// <summary>
        /// Image digests (sha256 content hashes).
        /// </summary>
        public List<string> Digests { get; set; } = new List<string>();

        /// <summary>
        /// Created timestamp.
        /// </summary>
        public DateTime Created { get; set; }

        /// <summary>
        /// Image size in bytes.
        /// </summary>
        public long Size { get; set; }

        /// <summary>
        /// Virtual size (includes shared layers).
        /// </summary>
        public long VirtualSize { get; set; }

        /// <summary>
        /// Image labels.
        /// </summary>
        public Dictionary<string, string> Labels { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Operating system.
        /// </summary>
        public string Os { get; set; }

        /// <summary>
        /// Architecture (e.g., "amd64", "arm64").
        /// </summary>
        public string Architecture { get; set; }

        /// <summary>
        /// Docker version used to build the image.
        /// </summary>
        public string DockerVersion { get; set; }

        /// <summary>
        /// Author of the image.
        /// </summary>
        public string Author { get; set; }

        /// <summary>
        /// Parent image ID.
        /// </summary>
        public string ParentId { get; set; }
    }
}
