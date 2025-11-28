namespace FluentDocker.Model.Drivers
{
    /// <summary>
    /// Describes the capabilities supported by a driver.
    /// </summary>
    public class DriverCapabilities
    {
        /// <summary>
        /// Driver supports container operations.
        /// </summary>
        public bool SupportsContainers { get; set; }

        /// <summary>
        /// Driver supports image operations.
        /// </summary>
        public bool SupportsImages { get; set; }

        /// <summary>
        /// Driver supports network operations.
        /// </summary>
        public bool SupportsNetworks { get; set; }

        /// <summary>
        /// Driver supports volume operations.
        /// </summary>
        public bool SupportsVolumes { get; set; }

        /// <summary>
        /// Driver supports compose operations.
        /// </summary>
        public bool SupportsCompose { get; set; }

        /// <summary>
        /// Driver supports system operations (info, version, events).
        /// </summary>
        public bool SupportsSystem { get; set; }

        /// <summary>
        /// Driver supports pod operations (Podman-specific).
        /// </summary>
        public bool SupportsPods { get; set; }

        /// <summary>
        /// Driver version string.
        /// </summary>
        public string Version { get; set; }

        /// <summary>
        /// API version (if applicable).
        /// </summary>
        public string ApiVersion { get; set; }

        /// <summary>
        /// Creates default capabilities (all supported).
        /// </summary>
        public static DriverCapabilities Default()
        {
            return new DriverCapabilities
            {
                SupportsContainers = true,
                SupportsImages = true,
                SupportsNetworks = true,
                SupportsVolumes = true,
                SupportsCompose = true,
                SupportsSystem = true,
                SupportsPods = false
            };
        }
    }
}
