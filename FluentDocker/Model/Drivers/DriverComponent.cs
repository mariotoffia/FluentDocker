namespace FluentDocker.Model.Drivers
{
    /// <summary>
    /// Specifies the driver component/capability type.
    /// </summary>
    public enum DriverComponent
    {
        /// <summary>
        /// Container operations (create, start, stop, etc.)
        /// </summary>
        Container,

        /// <summary>
        /// Image operations (pull, push, build, etc.)
        /// </summary>
        Image,

        /// <summary>
        /// Network operations (create, connect, disconnect, etc.)
        /// </summary>
        Network,

        /// <summary>
        /// Volume operations (create, remove, etc.)
        /// </summary>
        Volume,

        /// <summary>
        /// Docker Compose / Podman Compose operations
        /// </summary>
        Compose,

        /// <summary>
        /// System operations (info, version, events, etc.)
        /// </summary>
        System,

        /// <summary>
        /// Podman-specific: Pod operations
        /// </summary>
        Pod,

        /// <summary>
        /// Podman-specific: Kubernetes YAML operations (play, down, generate)
        /// </summary>
        Kubernetes,

        /// <summary>
        /// Podman-specific: Machine management (init, start, stop, etc.)
        /// </summary>
        Machine
    }
}
