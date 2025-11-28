namespace FluentDocker.Model.Drivers
{
    /// <summary>
    /// Specifies the type of container runtime driver.
    /// </summary>
    public enum DriverType
    {
        /// <summary>
        /// Docker CLI driver - uses docker command-line interface
        /// </summary>
        DockerCli,

        /// <summary>
        /// Docker API driver - uses Docker Engine HTTP API
        /// </summary>
        DockerApi,

        /// <summary>
        /// Podman CLI driver - uses podman command-line interface
        /// </summary>
        PodmanCli,

        /// <summary>
        /// Podman API driver - uses Podman HTTP API
        /// </summary>
        PodmanApi,

        /// <summary>
        /// Custom driver implementation
        /// </summary>
        Custom
    }
}
