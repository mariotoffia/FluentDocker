using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using FluentDocker.Common;
using FluentDocker.Model.Containers;
using FluentDocker.Services;

namespace FluentDocker.Builders
{
    /// <summary>
    /// Container builder for lambda configuration.
    /// </summary>
    public interface IContainerBuilder
    {
        #region Basic Configuration

        IContainerBuilder UseImage(string image);
        IContainerBuilder WithName(string name);
        IContainerBuilder WithEnvironment(string key, string value);
        /// <summary>Sets an environment variable using "KEY=VALUE" format.</summary>
        IContainerBuilder WithEnvironment(string keyValue);
        IContainerBuilder WithPort(string containerPort, string hostPort);
        /// <summary>Exposes a container port, letting Docker assign a random host port.</summary>
        IContainerBuilder ExposePort(string containerPort);
        /// <summary>Exposes a container port with explicit host port mapping.</summary>
        IContainerBuilder ExposePort(int hostPort, int containerPort);
        IContainerBuilder WithCommand(params string[] command);
        IContainerBuilder WithVolume(string hostPath, string containerPath);
        IContainerBuilder WithLabel(string key, string value);
        IContainerBuilder WithWorkingDirectory(string workingDir);
        IContainerBuilder WithUser(string user);
        IContainerBuilder WithRestartPolicy(string policy);
        IContainerBuilder WithHostname(string hostname);
        IContainerBuilder WithNetworkMode(string networkMode);
        IContainerBuilder WithNetwork(string networkName);
        /// <summary>Adds the container to a network with a DNS alias.</summary>
        IContainerBuilder WithNetworkAlias(string networkName, string alias);

        /// <summary>
        /// Sets a static IPv4 address for the container.
        /// Requires the container to be connected to a custom network with a defined subnet.
        /// </summary>
        /// <param name="ipv4Address">The IPv4 address to assign (e.g., "10.18.0.22").</param>
        IContainerBuilder UseIpV4(string ipv4Address);

        /// <summary>
        /// Sets a static IPv6 address for the container.
        /// Requires the container to be connected to an IPv6-enabled custom network.
        /// </summary>
        /// <param name="ipv6Address">The IPv6 address to assign.</param>
        IContainerBuilder UseIpV6(string ipv6Address);

        IContainerBuilder WithMemoryLimit(long bytes);
        IContainerBuilder WithCpuShares(long shares);
        IContainerBuilder WithPrivileged(bool privileged = true);
        IContainerBuilder WithAutoRemove(bool autoRemove = true);

        /// <summary>Links this container to another container (legacy Docker feature).</summary>
        /// <param name="containerName">Name of the container to link to</param>
        /// <param name="alias">Optional alias for the linked container</param>
        /// <remarks>
        /// Container linking is a legacy Docker feature. Consider using user-defined networks instead.
        /// Links allow containers to discover each other and securely transfer information about one container to another.
        /// </remarks>
        IContainerBuilder WithLink(string containerName, string alias = null);

        /// <summary>Links this container to multiple other containers (legacy Docker feature).</summary>
        /// <param name="containerNames">Names of the containers to link to</param>
        IContainerBuilder WithLinks(params string[] containerNames);

        /// <summary>Associates this container with a Podman pod. Ignored by Docker.</summary>
        /// <param name="podName">Name of the pod to join.</param>
        IContainerBuilder WithPod(string podName);

        #endregion

        #region Container Existence Behavior

        /// <summary>If container with same name exists, reuse it instead of creating new.</summary>
        IContainerBuilder ReuseIfExists();

        /// <summary>If container with same name exists, destroy it before creating new.</summary>
        /// <param name="force">Force remove even if running.</param>
        /// <param name="removeVolumes">Remove associated volumes.</param>
        IContainerBuilder DestroyIfExists(bool force = false, bool removeVolumes = false);

        /// <summary>Always pull the image before creating container.</summary>
        IContainerBuilder ForcePullImage();

        #endregion

        #region Wait Conditions

        /// <summary>Wait for a port to be available after starting.</summary>
        IContainerBuilder WaitForPort(string portAndProto, long timeoutMs = 30000);

        /// <summary>Wait for a port with custom address.</summary>
        IContainerBuilder WaitForPort(string portAndProto, string address, long timeoutMs = 30000);

        /// <summary>Wait for a process to be running after starting.</summary>
        IContainerBuilder WaitForProcess(string processName, long timeoutMs = 30000);

        /// <summary>Wait for an HTTP endpoint to respond after starting.</summary>
        IContainerBuilder WaitForHttp(string portAndProto, string path = "/", long timeoutMs = 30000);

        /// <summary>Wait for an HTTP endpoint with advanced options.</summary>
        IContainerBuilder WaitForHttp(
            string url,
            long timeoutMs = 30000,
            HttpMethod method = null,
            string contentType = null,
            string body = null,
            Func<RequestResponse, int, long> continuation = null);

        /// <summary>Wait for a specific message in logs after starting.</summary>
        IContainerBuilder WaitForLogMessage(string message, long timeoutMs = 30000);

        /// <summary>Wait for container to be healthy (Docker HEALTHCHECK).</summary>
        IContainerBuilder WaitForHealthy(long timeoutMs = 30000);

        /// <summary>Custom wait condition lambda.</summary>
        /// <param name="condition">Function that returns poll interval in ms, or 0 to continue, or -1 to succeed.</param>
        IContainerBuilder Wait(Func<IContainerService, int, int> condition);

        #endregion

        #region Lifecycle Hooks

        /// <summary>Copy file/folder to container after it starts.</summary>
        IContainerBuilder CopyToOnStart(string hostPath, string containerPath);

        /// <summary>Copy file/folder from container before it's disposed.</summary>
        IContainerBuilder CopyFromOnDispose(string containerPath, string hostPath);

        /// <summary>Export container filesystem on dispose.</summary>
        IContainerBuilder ExportOnDispose(string hostPath, bool explode = false);

        /// <summary>Export container filesystem on dispose with condition.</summary>
        IContainerBuilder ExportOnDispose(string hostPath, Func<IContainerService, bool> condition, bool explode = false);

        /// <summary>Execute command in container after it starts.</summary>
        IContainerBuilder ExecuteOnRunning(params string[] command);

        /// <summary>Execute command in container before it's disposed.</summary>
        IContainerBuilder ExecuteOnDisposing(params string[] command);

        #endregion

        #region Dispose Behavior

        /// <summary>Keeps the container after dispose (don't delete).</summary>
        IContainerBuilder KeepContainer();

        /// <summary>Keeps the container running after dispose (don't stop).</summary>
        IContainerBuilder KeepRunning();

        /// <summary>Delete anonymous volumes when container is disposed.</summary>
        IContainerBuilder DeleteVolumeOnDispose();

        /// <summary>Delete named volumes when container is disposed.</summary>
        IContainerBuilder DeleteNamedVolumeOnDispose();

        #endregion

        #region Advanced

        /// <summary>Use custom endpoint resolver for port mapping.</summary>
        IContainerBuilder UseCustomResolver(
            Func<Dictionary<string, HostIpEndpoint[]>, string, Uri, IPEndPoint> resolver);

        #endregion
    }
}
