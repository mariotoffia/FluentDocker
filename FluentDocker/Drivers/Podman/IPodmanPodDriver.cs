using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Drivers.Podman
{
    /// <summary>
    /// Podman-specific driver interface for pod operations.
    /// Pods group containers that share namespaces (network, PID, etc.),
    /// similar to Kubernetes pods.
    /// </summary>
    public interface IPodmanPodDriver
    {
        /// <summary>Creates a new pod.</summary>
        Task<CommandResponse<PodCreateResult>> CreatePodAsync(
            DriverContext context, PodCreateConfig config,
            CancellationToken cancellationToken = default);

        /// <summary>Starts a pod and all its containers.</summary>
        Task<CommandResponse<Unit>> StartPodAsync(
            DriverContext context, string name,
            CancellationToken cancellationToken = default);

        /// <summary>Stops a pod and all its containers.</summary>
        Task<CommandResponse<Unit>> StopPodAsync(
            DriverContext context, string name, int? timeout = null,
            CancellationToken cancellationToken = default);

        /// <summary>Restarts a pod and all its containers.</summary>
        Task<CommandResponse<Unit>> RestartPodAsync(
            DriverContext context, string name, int? timeout = null,
            CancellationToken cancellationToken = default);

        /// <summary>Sends a signal to a pod's containers.</summary>
        Task<CommandResponse<Unit>> KillPodAsync(
            DriverContext context, string name, string signal = null,
            CancellationToken cancellationToken = default);

        /// <summary>Pauses all containers in a pod.</summary>
        Task<CommandResponse<Unit>> PausePodAsync(
            DriverContext context, string name,
            CancellationToken cancellationToken = default);

        /// <summary>Unpauses all containers in a pod.</summary>
        Task<CommandResponse<Unit>> UnpausePodAsync(
            DriverContext context, string name,
            CancellationToken cancellationToken = default);

        /// <summary>Removes a pod and optionally its containers.</summary>
        Task<CommandResponse<Unit>> RemovePodAsync(
            DriverContext context, string name, bool force = false,
            CancellationToken cancellationToken = default);

        /// <summary>Lists pods.</summary>
        Task<CommandResponse<IList<PodInfo>>> ListPodsAsync(
            DriverContext context,
            CancellationToken cancellationToken = default);

        /// <summary>Inspects a pod returning detailed information.</summary>
        Task<CommandResponse<PodInspectResult>> InspectPodAsync(
            DriverContext context, string name,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Configuration for creating a pod.
    /// </summary>
    public class PodCreateConfig
    {
        /// <summary>Pod name.</summary>
        public string Name { get; set; }

        /// <summary>Labels to attach to the pod.</summary>
        public Dictionary<string, string> Labels { get; set; } = new Dictionary<string, string>();

        /// <summary>Network to connect the pod to.</summary>
        public string Network { get; set; }

        /// <summary>Hostname of the pod.</summary>
        public string Hostname { get; set; }

        /// <summary>DNS servers for the pod.</summary>
        public List<string> Dns { get; set; } = new List<string>();

        /// <summary>Custom infra container image.</summary>
        public string InfraImage { get; set; }

        /// <summary>Namespaces to share (e.g., "ipc,net,uts").</summary>
        public string Share { get; set; }

        /// <summary>Port mappings for the pod (e.g., "8080:80").</summary>
        public List<string> Ports { get; set; } = new List<string>();
    }

    /// <summary>Result of creating a pod.</summary>
    public class PodCreateResult
    {
        /// <summary>Pod identifier.</summary>
        public string Id { get; set; }
    }

    /// <summary>
    /// Summary information about a pod.
    /// </summary>
    public class PodInfo
    {
        /// <summary>Pod identifier.</summary>
        public string Id { get; set; }

        /// <summary>Pod name.</summary>
        public string Name { get; set; }

        /// <summary>Pod status (e.g., Running, Stopped, Created).</summary>
        public string Status { get; set; }

        /// <summary>Creation timestamp.</summary>
        public string Created { get; set; }

        /// <summary>Infra container identifier.</summary>
        public string InfraId { get; set; }

        /// <summary>Number of containers in the pod.</summary>
        public int NumContainers { get; set; }

        /// <summary>Container summaries within this pod.</summary>
        public IList<PodContainerInfo> Containers { get; set; } = new List<PodContainerInfo>();
    }

    /// <summary>
    /// Detailed inspection result for a pod.
    /// </summary>
    public class PodInspectResult
    {
        /// <summary>Pod identifier.</summary>
        public string Id { get; set; }

        /// <summary>Pod name.</summary>
        public string Name { get; set; }

        /// <summary>Pod state (e.g., Running, Stopped, Created).</summary>
        public string State { get; set; }

        /// <summary>Creation timestamp.</summary>
        public string Created { get; set; }

        /// <summary>Pod hostname.</summary>
        public string Hostname { get; set; }

        /// <summary>Infra container identifier.</summary>
        public string InfraContainerId { get; set; }

        /// <summary>Number of containers in the pod.</summary>
        public int NumContainers { get; set; }

        /// <summary>Containers within this pod.</summary>
        public IList<PodContainerInfo> Containers { get; set; } = new List<PodContainerInfo>();
    }

    /// <summary>
    /// Information about a container within a pod.
    /// </summary>
    public class PodContainerInfo
    {
        /// <summary>Container identifier.</summary>
        public string Id { get; set; }

        /// <summary>Container name.</summary>
        public string Name { get; set; }

        /// <summary>Container state (e.g., running, exited).</summary>
        public string State { get; set; }
    }
}
