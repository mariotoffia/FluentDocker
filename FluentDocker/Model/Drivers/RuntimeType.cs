namespace FluentDocker.Model.Drivers
{
    /// <summary>
    /// Specifies the container runtime type.
    /// </summary>
    public enum RuntimeType
    {
        /// <summary>
        /// Docker runtime
        /// </summary>
        Docker,

        /// <summary>
        /// Podman runtime
        /// </summary>
        Podman,

        /// <summary>
        /// Containerd runtime
        /// </summary>
        Containerd,

        /// <summary>
        /// CRI-O runtime
        /// </summary>
        CriO,

        /// <summary>
        /// Unknown or custom runtime
        /// </summary>
        Unknown
    }
}
