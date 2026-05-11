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
  /// Fluent interface for configuring a container before creation.
  /// All methods return the builder instance for method chaining.
  /// </summary>
  /// <remarks>
  /// <para>
  /// Use this interface within a <see cref="Builder"/> lambda to configure container settings
  /// such as image, ports, volumes, environment variables, wait conditions, and lifecycle hooks.
  /// </para>
  /// <para>
  /// Example usage:
  /// <code>
  /// builder.UseContainer(c => c
  ///     .UseImage("postgres:15-alpine")
  ///     .WithName("my-db")
  ///     .WithEnvironment("POSTGRES_PASSWORD", "secret")
  ///     .ExposePort(5432, 5432)
  ///     .WaitForPort("5432/tcp"));
  /// </code>
  /// </para>
  /// </remarks>
  public interface IContainerBuilder
  {
    #region Basic Configuration

    /// <summary>Sets the container image (e.g. "nginx:latest", "postgres:15-alpine").</summary>
    /// <param name="image">The image name with optional tag or digest.</param>
    /// <returns>The builder instance for method chaining.</returns>
    IContainerBuilder UseImage(string image);

    /// <summary>Sets the container name.</summary>
    /// <param name="name">
    /// A unique name for the container. Must match the pattern [a-zA-Z0-9][a-zA-Z0-9_.-].
    /// </param>
    /// <returns>The builder instance for method chaining.</returns>
    IContainerBuilder WithName(string name);

    /// <summary>Sets an environment variable by key and value.</summary>
    /// <param name="key">The environment variable name (e.g. "POSTGRES_PASSWORD").</param>
    /// <param name="value">The environment variable value.</param>
    /// <returns>The builder instance for method chaining.</returns>
    IContainerBuilder WithEnvironment(string key, string value);

    /// <summary>Sets an environment variable using "KEY=VALUE" format.</summary>
    /// <param name="keyValue">
    /// The environment variable in "KEY=VALUE" format (e.g. "POSTGRES_PASSWORD=secret").
    /// </param>
    /// <returns>The builder instance for method chaining.</returns>
    IContainerBuilder WithEnvironment(string keyValue);

    /// <summary>Maps a container port to a specific host port.</summary>
    /// <param name="containerPort">
    /// The container port with optional protocol (e.g. "8080/tcp", "53/udp").
    /// </param>
    /// <param name="hostPort">The host port to bind to (e.g. "8080").</param>
    /// <returns>The builder instance for method chaining.</returns>
    IContainerBuilder WithPort(string containerPort, string hostPort);

    /// <summary>
    /// Exposes a container port, letting Docker/Podman assign a random host port.
    /// </summary>
    /// <param name="containerPort">
    /// The container port with optional protocol (e.g. "8080/tcp", "53/udp").
    /// </param>
    /// <returns>The builder instance for method chaining.</returns>
    IContainerBuilder ExposePort(string containerPort);

    /// <summary>Exposes a container port with explicit host port mapping.</summary>
    /// <param name="hostPort">The port on the host to bind to.</param>
    /// <param name="containerPort">The port inside the container to expose.</param>
    /// <returns>The builder instance for method chaining.</returns>
    IContainerBuilder ExposePort(int hostPort, int containerPort);

    /// <summary>Sets the command to run in the container, overriding the image's default CMD.</summary>
    /// <param name="command">
    /// The command and its arguments. Each element is a separate argument
    /// (e.g. <c>"sh", "-c", "echo hello"</c>).
    /// </param>
    /// <returns>The builder instance for method chaining.</returns>
    IContainerBuilder WithCommand(params string[] command);

    /// <summary>Binds a host path to a container path as a volume mount.</summary>
    /// <param name="hostPath">The absolute path on the host filesystem.</param>
    /// <param name="containerPath">The mount point inside the container.</param>
    /// <returns>The builder instance for method chaining.</returns>
    IContainerBuilder WithVolume(string hostPath, string containerPath);

    /// <summary>Adds a label to the container.</summary>
    /// <param name="key">The label key.</param>
    /// <param name="value">The label value.</param>
    /// <returns>The builder instance for method chaining.</returns>
    IContainerBuilder WithLabel(string key, string value);

    /// <summary>Sets the working directory inside the container.</summary>
    /// <param name="workingDir">The absolute path to use as the working directory.</param>
    /// <returns>The builder instance for method chaining.</returns>
    IContainerBuilder WithWorkingDirectory(string workingDir);

    /// <summary>Sets the user (UID or username) to run the container process.</summary>
    /// <param name="user">
    /// A UID, username, or "UID:GID" / "user:group" combination (e.g. "1000", "www-data", "1000:1000").
    /// </param>
    /// <returns>The builder instance for method chaining.</returns>
    IContainerBuilder WithUser(string user);

    /// <summary>Sets the restart policy (e.g. "no", "always", "on-failure:5", "unless-stopped").</summary>
    /// <param name="policy">The restart policy string.</param>
    /// <returns>The builder instance for method chaining.</returns>
    IContainerBuilder WithRestartPolicy(string policy);

    /// <summary>Sets the container hostname.</summary>
    /// <param name="hostname">The hostname to assign inside the container.</param>
    /// <returns>The builder instance for method chaining.</returns>
    IContainerBuilder WithHostname(string hostname);

    /// <summary>Sets the network mode (e.g. "bridge", "host", "none", "container:name").</summary>
    /// <param name="networkMode">The networking mode for the container.</param>
    /// <returns>The builder instance for method chaining.</returns>
    IContainerBuilder WithNetworkMode(string networkMode);

    /// <summary>Connects the container to a named network.</summary>
    /// <param name="networkName">The name of an existing Docker/Podman network.</param>
    /// <returns>The builder instance for method chaining.</returns>
    IContainerBuilder WithNetwork(string networkName);

    /// <summary>Adds the container to a network with a DNS alias.</summary>
    /// <param name="networkName">The name of an existing Docker/Podman network.</param>
    /// <param name="alias">A DNS alias for this container on the specified network.</param>
    /// <returns>The builder instance for method chaining.</returns>
#pragma warning disable CA1716 // Parameter 'alias' conflicts with reserved keyword — intentional API design
    IContainerBuilder WithNetworkAlias(string networkName, string alias);
#pragma warning restore CA1716

    /// <summary>
    /// Sets a static IPv4 address for the container.
    /// Requires the container to be connected to a custom network with a defined subnet.
    /// </summary>
    /// <param name="ipv4Address">The IPv4 address to assign (e.g., "10.18.0.22").</param>
    /// <returns>The builder instance for method chaining.</returns>
    IContainerBuilder WithIPv4(string ipv4Address);

    /// <summary>
    /// Sets a static IPv6 address for the container.
    /// Requires the container to be connected to an IPv6-enabled custom network.
    /// </summary>
    /// <param name="ipv6Address">The IPv6 address to assign.</param>
    /// <returns>The builder instance for method chaining.</returns>
    IContainerBuilder WithIPv6(string ipv6Address);

    /// <summary>Sets the memory limit in bytes.</summary>
    /// <param name="bytes">The maximum memory in bytes (e.g. 536870912 for 512 MB).</param>
    /// <returns>The builder instance for method chaining.</returns>
    IContainerBuilder WithMemoryLimit(long bytes);

    /// <summary>Sets the CPU shares (relative weight for CPU scheduling).</summary>
    /// <param name="shares">
    /// Relative CPU weight. The default is 1024. A container with 512 shares gets half the CPU time
    /// of a container with 1024 shares when they compete for cycles.
    /// </param>
    /// <returns>The builder instance for method chaining.</returns>
    IContainerBuilder WithCpuShares(long shares);

    /// <summary>Runs the container in privileged mode with full host capabilities.</summary>
    /// <param name="privileged">
    /// <c>true</c> to enable privileged mode; <c>false</c> to disable. Defaults to <c>true</c>.
    /// </param>
    /// <returns>The builder instance for method chaining.</returns>
    IContainerBuilder WithPrivileged(bool privileged = true);

    /// <summary>Automatically removes the container when it exits.</summary>
    /// <param name="autoRemove">
    /// <c>true</c> to enable auto-removal; <c>false</c> to disable. Defaults to <c>true</c>.
    /// </param>
    /// <returns>The builder instance for method chaining.</returns>
    IContainerBuilder WithAutoRemove(bool autoRemove = true);

    /// <summary>Links this container to another container (legacy Docker feature).</summary>
    /// <param name="containerName">Name of the container to link to.</param>
    /// <param name="alias">Optional alias for the linked container.</param>
    /// <returns>The builder instance for method chaining.</returns>
    /// <remarks>
    /// Container linking is a legacy Docker feature. Consider using user-defined networks instead.
    /// Links allow containers to discover each other and securely transfer information about one container to another.
    /// </remarks>
#pragma warning disable CA1716 // Parameter 'alias' conflicts with reserved keyword — intentional API design
    IContainerBuilder WithLink(string containerName, string alias = null);
#pragma warning restore CA1716

    /// <summary>Links this container to multiple other containers (legacy Docker feature).</summary>
    /// <param name="containerNames">Names of the containers to link to.</param>
    /// <returns>The builder instance for method chaining.</returns>
    IContainerBuilder WithLinks(params string[] containerNames);

    /// <summary>Associates this container with a Podman pod. Ignored by Docker.</summary>
    /// <param name="podName">Name of the pod to join.</param>
    /// <returns>The builder instance for method chaining.</returns>
    IContainerBuilder WithPod(string podName);

    /// <summary>Adds a Linux capability (e.g. SYS_PTRACE, NET_ADMIN).</summary>
    /// <param name="capability">The capability name without the CAP_ prefix.</param>
    /// <returns>The builder instance for method chaining.</returns>
    IContainerBuilder WithCapAdd(string capability);

    /// <summary>Drops a Linux capability (e.g. NET_RAW, MKNOD).</summary>
    /// <param name="capability">The capability name without the CAP_ prefix.</param>
    /// <returns>The builder instance for method chaining.</returns>
    IContainerBuilder WithCapDrop(string capability);

    /// <summary>Adds a security option (e.g. seccomp=unconfined).</summary>
    /// <param name="option">The security option string.</param>
    /// <returns>The builder instance for method chaining.</returns>
#pragma warning disable CA1716 // Parameter 'option' conflicts with reserved keyword — intentional API design
    IContainerBuilder WithSecurityOpt(string option);
#pragma warning restore CA1716

    /// <summary>Sets the size of /dev/shm in bytes.</summary>
    /// <param name="bytes">The shared memory size in bytes.</param>
    /// <returns>The builder instance for method chaining.</returns>
    IContainerBuilder WithShmSize(long bytes);

    /// <summary>Adds a tmpfs mount at the given container path.</summary>
    /// <param name="containerPath">Path inside the container.</param>
    /// <param name="options">Mount options (e.g. "rw,noexec,size=64m"). Null for defaults.</param>
    /// <returns>The builder instance for method chaining.</returns>
    IContainerBuilder WithTmpfs(string containerPath, string options = null);

    /// <summary>Maps a host device into the container.</summary>
    /// <param name="hostDevice">Device path on the host (e.g. /dev/sda).</param>
    /// <param name="containerDevice">Device path in the container. Null uses the same path as host.</param>
    /// <returns>The builder instance for method chaining.</returns>
    IContainerBuilder WithDevice(string hostDevice, string containerDevice = null);

    /// <summary>Makes the root filesystem read-only.</summary>
    /// <returns>The builder instance for method chaining.</returns>
    IContainerBuilder WithReadonlyRootfs();

    /// <summary>Sets the platform for multi-arch images (e.g. linux/arm64).</summary>
    /// <param name="platform">The target platform in "os/architecture" format.</param>
    /// <returns>The builder instance for method chaining.</returns>
    IContainerBuilder WithPlatform(string platform);

    /// <summary>Sets the OCI runtime to use (e.g. runc, crun, runsc).</summary>
    /// <param name="runtime">The OCI runtime name.</param>
    /// <returns>The builder instance for method chaining.</returns>
    IContainerBuilder WithRuntime(string runtime);

    #endregion

    #region Container Existence Behavior

    /// <summary>If a container with the same name exists, reuse it instead of creating a new one.</summary>
    /// <returns>The builder instance for method chaining.</returns>
    IContainerBuilder ReuseIfExists();

    /// <summary>If a container with the same name exists, destroy it before creating a new one.</summary>
    /// <param name="force">Force remove even if the container is running.</param>
    /// <param name="removeVolumes">Remove associated anonymous volumes.</param>
    /// <returns>The builder instance for method chaining.</returns>
    IContainerBuilder DestroyIfExists(bool force = false, bool removeVolumes = false);

    /// <summary>Always pull the image before creating the container, even if it exists locally.</summary>
    /// <returns>The builder instance for method chaining.</returns>
    IContainerBuilder ForcePullImage();

    #endregion

    #region Wait Conditions

    /// <summary>
    /// Waits for a container port to accept connections after starting.
    /// </summary>
    /// <param name="portAndProto">The port and protocol (e.g. "5432/tcp", "53/udp").</param>
    /// <param name="timeoutMs">Maximum time to wait in milliseconds. Defaults to 30000 (30 seconds).</param>
    /// <returns>The builder instance for method chaining.</returns>
    IContainerBuilder WaitForPort(string portAndProto, long timeoutMs = 30000);

    /// <summary>
    /// Waits for a container port to accept connections at a specific address after starting.
    /// </summary>
    /// <param name="portAndProto">The port and protocol (e.g. "5432/tcp", "53/udp").</param>
    /// <param name="address">The IP address or hostname to connect to when probing the port.</param>
    /// <param name="timeoutMs">Maximum time to wait in milliseconds. Defaults to 30000 (30 seconds).</param>
    /// <returns>The builder instance for method chaining.</returns>
    IContainerBuilder WaitForPort(string portAndProto, string address, long timeoutMs = 30000);

    /// <summary>
    /// Waits for a named process to be running inside the container after starting.
    /// </summary>
    /// <param name="processName">The process name to look for (e.g. "postgres", "nginx").</param>
    /// <param name="timeoutMs">Maximum time to wait in milliseconds. Defaults to 30000 (30 seconds).</param>
    /// <returns>The builder instance for method chaining.</returns>
    IContainerBuilder WaitForProcess(string processName, long timeoutMs = 30000);

    /// <summary>
    /// Waits for an HTTP endpoint inside the container to return a successful response.
    /// </summary>
    /// <param name="portAndProto">The port and protocol (e.g. "8080/tcp").</param>
    /// <param name="path">The HTTP path to request. Defaults to "/".</param>
    /// <param name="timeoutMs">Maximum time to wait in milliseconds. Defaults to 30000 (30 seconds).</param>
    /// <returns>The builder instance for method chaining.</returns>
    IContainerBuilder WaitForHttp(string portAndProto, string path = "/", long timeoutMs = 30000);

    /// <summary>
    /// Waits for an HTTP endpoint with advanced options such as custom method, body, and continuation logic.
    /// </summary>
    /// <param name="url">The full URL to probe.</param>
    /// <param name="timeoutMs">Maximum time to wait in milliseconds. Defaults to 30000 (30 seconds).</param>
    /// <param name="method">The HTTP method to use. Defaults to GET when null.</param>
    /// <param name="contentType">The Content-Type header value for the request body.</param>
    /// <param name="body">The request body content.</param>
    /// <param name="continuation">
    /// A callback invoked after each HTTP response. Receives the <see cref="RequestResponse"/> and the
    /// current attempt count. Return a positive value in milliseconds to retry after that delay,
    /// 0 to continue immediately, or -1 to indicate success.
    /// </param>
    /// <returns>The builder instance for method chaining.</returns>
    IContainerBuilder WaitForHttp(
        string url,
        long timeoutMs = 30000,
        HttpMethod method = null,
        string contentType = null,
        string body = null,
        Func<RequestResponse, int, long> continuation = null);

    /// <summary>
    /// Waits for a specific message to appear in the container's log output after starting.
    /// </summary>
    /// <param name="message">The log message substring to wait for.</param>
    /// <param name="timeoutMs">Maximum time to wait in milliseconds. Defaults to 30000 (30 seconds).</param>
    /// <returns>The builder instance for method chaining.</returns>
    IContainerBuilder WaitForLogMessage(string message, long timeoutMs = 30000);

    /// <summary>
    /// Waits for the container's health check to report "healthy" (requires a HEALTHCHECK in the image).
    /// </summary>
    /// <param name="timeoutMs">Maximum time to wait in milliseconds. Defaults to 30000 (30 seconds).</param>
    /// <returns>The builder instance for method chaining.</returns>
    IContainerBuilder WaitForHealthy(long timeoutMs = 30000);

    /// <summary>Registers a custom wait condition evaluated in a polling loop.</summary>
    /// <param name="condition">
    /// A function receiving the <see cref="IContainerService"/> and the current attempt count (zero-based).
    /// Return a positive value in milliseconds to retry after that delay,
    /// 0 to continue polling immediately, or -1 to indicate success.
    /// </param>
    /// <returns>The builder instance for method chaining.</returns>
    IContainerBuilder Wait(Func<IContainerService, int, int> condition);

    /// <summary>
    /// Sets the poll interval for subsequent wait conditions.
    /// </summary>
    /// <param name="intervalMs">Delay in milliseconds between poll iterations (default 500).</param>
    /// <returns>The builder instance for method chaining.</returns>
    IContainerBuilder WithWaitPollInterval(int intervalMs);

    #endregion

    #region Lifecycle Hooks

    /// <summary>Copies a file or folder from the host to the container after it starts.</summary>
    /// <param name="hostPath">The source path on the host filesystem.</param>
    /// <param name="containerPath">The destination path inside the container.</param>
    /// <returns>The builder instance for method chaining.</returns>
    IContainerBuilder CopyToOnStart(string hostPath, string containerPath);

    /// <summary>Copies a file or folder from the container to the host before it is disposed.</summary>
    /// <param name="containerPath">The source path inside the container.</param>
    /// <param name="hostPath">The destination path on the host filesystem.</param>
    /// <returns>The builder instance for method chaining.</returns>
    IContainerBuilder CopyFromOnDispose(string containerPath, string hostPath);

    /// <summary>Exports the container filesystem as a tar archive when it is disposed.</summary>
    /// <param name="hostPath">The destination file path on the host for the tar archive.</param>
    /// <param name="explode">
    /// <c>true</c> to extract the archive contents into a directory; <c>false</c> to keep the tar file.
    /// </param>
    /// <returns>The builder instance for method chaining.</returns>
    IContainerBuilder ExportOnDispose(string hostPath, bool explode = false);

    /// <summary>
    /// Exports the container filesystem as a tar archive when disposed, but only if a condition is met.
    /// </summary>
    /// <param name="hostPath">The destination file path on the host for the tar archive.</param>
    /// <param name="condition">
    /// A predicate that receives the <see cref="IContainerService"/> and returns <c>true</c> to proceed
    /// with the export or <c>false</c> to skip it.
    /// </param>
    /// <param name="explode">
    /// <c>true</c> to extract the archive contents into a directory; <c>false</c> to keep the tar file.
    /// </param>
    /// <returns>The builder instance for method chaining.</returns>
    IContainerBuilder ExportOnDispose(string hostPath, Func<IContainerService, bool> condition, bool explode = false);

    /// <summary>Executes a command inside the container after it starts running.</summary>
    /// <param name="command">The command and its arguments to execute.</param>
    /// <returns>The builder instance for method chaining.</returns>
    IContainerBuilder ExecuteOnRunning(params string[] command);

    /// <summary>Executes a command inside the container before it is disposed.</summary>
    /// <param name="command">The command and its arguments to execute.</param>
    /// <returns>The builder instance for method chaining.</returns>
    IContainerBuilder ExecuteOnDisposing(params string[] command);

    #endregion

    #region Dispose Behavior

    /// <summary>Keeps the container after the service is disposed (does not delete it).</summary>
    /// <returns>The builder instance for method chaining.</returns>
    IContainerBuilder KeepContainer();

    /// <summary>Keeps the container running after the service is disposed (does not stop it).</summary>
    /// <returns>The builder instance for method chaining.</returns>
    IContainerBuilder KeepRunning();

    /// <summary>Deletes anonymous volumes when the container is disposed.</summary>
    /// <returns>The builder instance for method chaining.</returns>
    IContainerBuilder DeleteVolumeOnDispose();

    /// <summary>Deletes named volumes when the container is disposed.</summary>
    /// <returns>The builder instance for method chaining.</returns>
    IContainerBuilder DeleteNamedVolumeOnDispose();

    #endregion

    #region Advanced

    /// <summary>
    /// Registers a custom endpoint resolver for translating container port mappings to host endpoints.
    /// </summary>
    /// <param name="resolver">
    /// A function that receives the port mapping dictionary, the requested port/protocol string,
    /// the Docker host URI, and returns the resolved <see cref="IPEndPoint"/>.
    /// </param>
    /// <returns>The builder instance for method chaining.</returns>
    IContainerBuilder UseCustomResolver(
        Func<Dictionary<string, HostIpEndpoint[]>, string, Uri, IPEndPoint> resolver);

    #endregion
  }
}
