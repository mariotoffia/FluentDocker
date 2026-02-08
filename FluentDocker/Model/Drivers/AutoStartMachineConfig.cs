namespace FluentDocker.Model.Drivers
{
    /// <summary>
    /// Configuration for automatic Podman machine start during driver initialization.
    /// When set on <see cref="DriverContext.AutoStartMachine"/>, the Podman driver pack
    /// will ensure a machine is running before completing initialization.
    /// </summary>
    public class AutoStartMachineConfig
    {
        /// <summary>
        /// Name of the machine to start. When null, the default machine is used.
        /// </summary>
        public string MachineName { get; set; }

        /// <summary>
        /// When true, initializes a new machine if none exists matching <see cref="MachineName"/>.
        /// When false (default), throws if no matching machine is found.
        /// </summary>
        public bool CreateIfNotExists { get; set; }

        /// <summary>
        /// Number of CPUs to allocate when creating a new machine.
        /// Only used when <see cref="CreateIfNotExists"/> is true.
        /// </summary>
        public int? InitCpus { get; set; }

        /// <summary>
        /// Memory in MiB to allocate when creating a new machine.
        /// Only used when <see cref="CreateIfNotExists"/> is true.
        /// </summary>
        public int? InitMemoryMiB { get; set; }

        /// <summary>
        /// Disk size in GiB to allocate when creating a new machine.
        /// Only used when <see cref="CreateIfNotExists"/> is true.
        /// </summary>
        public int? InitDiskSizeGiB { get; set; }

        /// <summary>
        /// Whether to create the machine in rootful mode.
        /// Only used when <see cref="CreateIfNotExists"/> is true.
        /// </summary>
        public bool InitRootful { get; set; }
    }
}
