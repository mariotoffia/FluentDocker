namespace FluentDocker.Drivers.Podman.Cli.Binary
{
    /// <summary>
    /// Interface for resolving Podman binaries on the local machine.
    /// </summary>
    public interface IPodmanBinaryResolver
    {
        /// <summary>
        /// Gets all resolved Podman binaries.
        /// </summary>
        PodmanBinary[] Binaries { get; }

        /// <summary>
        /// Gets the main Podman client binary.
        /// </summary>
        PodmanBinary MainPodmanClient { get; }

        /// <summary>
        /// Gets the Podman remote binary (if available).
        /// </summary>
        PodmanBinary PodmanRemote { get; }

        /// <summary>
        /// Resolves a Podman binary by command name.
        /// </summary>
        /// <param name="binary">The binary name (e.g., "podman").</param>
        /// <returns>The resolved PodmanBinary.</returns>
        PodmanBinary Resolve(string binary);

        /// <summary>
        /// Resolves the full path to a Podman binary, including sudo prefix if configured.
        /// </summary>
        /// <param name="podmanCommand">The Podman command name.</param>
        /// <returns>The command string ready for execution.</returns>
        string ResolveBinaryPath(string podmanCommand);
    }
}
