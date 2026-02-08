namespace FluentDocker.Drivers.Podman.Cli.Binary
{
    /// <summary>
    /// Represents the type of Podman binary.
    /// </summary>
    public enum PodmanBinaryType
    {
        /// <summary>
        /// The main Podman client binary (podman/podman.exe).
        /// </summary>
        PodmanClient = 1,

        /// <summary>
        /// Podman remote client binary (podman-remote).
        /// Used for connecting to remote Podman instances or machines.
        /// </summary>
        PodmanRemote = 2
    }
}
