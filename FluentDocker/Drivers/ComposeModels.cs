using System.Collections.Generic;

namespace FluentDocker.Drivers
{
    #region Config Types

    /// <summary>
    /// Base configuration for compose operations.
    /// </summary>
    public class ComposeFileConfig
    {
        /// <summary>Path to compose file(s).</summary>
        public List<string> ComposeFiles { get; set; } = new List<string>();
        /// <summary>Project name.</summary>
        public string ProjectName { get; set; }
        /// <summary>Project directory.</summary>
        public string ProjectDirectory { get; set; }
        /// <summary>Environment variables.</summary>
        public Dictionary<string, string> Environment { get; set; } = new Dictionary<string, string>();
        /// <summary>Specific services to target.</summary>
        public List<string> Services { get; set; } = new List<string>();
    }

    /// <summary>
    /// Configuration for compose up operation.
    /// </summary>
    public class ComposeUpConfig : ComposeFileConfig
    {
        /// <summary>Build images before starting.</summary>
        public bool Build { get; set; }
        /// <summary>Force recreate containers.</summary>
        public bool ForceRecreate { get; set; }
        /// <summary>Don't recreate containers.</summary>
        public bool NoRecreate { get; set; }
        /// <summary>Detached mode.</summary>
        public bool Detached { get; set; } = true;
        /// <summary>Remove orphan containers.</summary>
        public bool RemoveOrphans { get; set; }
        /// <summary>Don't build images.</summary>
        public bool NoBuild { get; set; }
        /// <summary>Don't start linked services.</summary>
        public bool NoDeps { get; set; }
        /// <summary>Don't start the services.</summary>
        public bool NoStart { get; set; }
        /// <summary>Wait for services to be healthy.</summary>
        public bool Wait { get; set; }
        /// <summary>Wait timeout in seconds.</summary>
        public int? WaitTimeout { get; set; }
        /// <summary>Shutdown timeout in seconds.</summary>
        public int? Timeout { get; set; }
        /// <summary>Pull image policy (always, missing, never).</summary>
        public string Pull { get; set; }
        /// <summary>Scale service replicas (service=count) for the up command.</summary>
        public Dictionary<string, int> Scale { get; set; } = new Dictionary<string, int>();
        /// <summary>Compose profiles to activate.</summary>
        public List<string> Profiles { get; set; } = new List<string>();
    }

    /// <summary>
    /// Configuration for compose down operation.
    /// </summary>
    public class ComposeDownConfig : ComposeFileConfig
    {
        /// <summary>Remove volumes.</summary>
        public bool RemoveVolumes { get; set; }
        /// <summary>Remove images (all, local).</summary>
        public string RemoveImages { get; set; }
        /// <summary>Timeout in seconds.</summary>
        public int? Timeout { get; set; }
        /// <summary>Remove orphaned containers.</summary>
        public bool RemoveOrphans { get; set; }
    }

    /// <summary>
    /// Configuration for compose stop operation.
    /// </summary>
    public class ComposeStopConfig : ComposeFileConfig
    {
        /// <summary>Timeout in seconds before forcing stop.</summary>
        public int? Timeout { get; set; }
    }

    /// <summary>
    /// Configuration for compose restart operation.
    /// </summary>
    public class ComposeRestartConfig : ComposeFileConfig
    {
        /// <summary>Timeout in seconds.</summary>
        public int? Timeout { get; set; }
        /// <summary>Don't restart linked services.</summary>
        public bool NoDeps { get; set; }
    }

    /// <summary>
    /// Configuration for compose kill operation.
    /// </summary>
    public class ComposeKillConfig : ComposeFileConfig
    {
        /// <summary>Signal to send (default: SIGKILL).</summary>
        public string Signal { get; set; } = "SIGKILL";
    }

    /// <summary>
    /// Configuration for compose rm operation.
    /// </summary>
    public class ComposeRemoveConfig : ComposeFileConfig
    {
        /// <summary>Don't ask to confirm removal.</summary>
        public bool Force { get; set; }
        /// <summary>Stop containers before removing.</summary>
        public bool Stop { get; set; }
        /// <summary>Remove anonymous volumes.</summary>
        public bool Volumes { get; set; }
    }

    /// <summary>
    /// Configuration for compose ps operation.
    /// </summary>
    public class ComposeListConfig : ComposeFileConfig
    {
        /// <summary>Show all containers (default: running only).</summary>
        public bool All { get; set; }
        /// <summary>Output format (json, table).</summary>
        public string Format { get; set; }
        /// <summary>Only display IDs.</summary>
        public bool Quiet { get; set; }
        /// <summary>Filter by status.</summary>
        public string Status { get; set; }
    }

    /// <summary>
    /// Configuration for compose logs operation.
    /// </summary>
    public class ComposeLogsConfig : ComposeFileConfig
    {
        /// <summary>Follow log output.</summary>
        public bool Follow { get; set; }
        /// <summary>Show timestamps.</summary>
        public bool Timestamps { get; set; }
        /// <summary>Number of lines to show from end.</summary>
        public int? Tail { get; set; }
        /// <summary>Show logs since timestamp.</summary>
        public string Since { get; set; }
        /// <summary>Show logs until timestamp.</summary>
        public string Until { get; set; }
        /// <summary>Don't colorize output.</summary>
        public bool NoColor { get; set; }
    }

    /// <summary>
    /// Configuration for compose config operation.
    /// </summary>
    public class ComposeConfigConfig : ComposeFileConfig
    {
        /// <summary>Only show service names.</summary>
        public bool ShowServices { get; set; }
        /// <summary>Only show volumes.</summary>
        public bool ShowVolumes { get; set; }
        /// <summary>Print resolved file paths.</summary>
        public bool ResolveImageDigests { get; set; }
        /// <summary>Output format.</summary>
        public string Format { get; set; }
    }

    /// <summary>
    /// Configuration for compose build operation.
    /// </summary>
    public class ComposeBuildConfig : ComposeFileConfig
    {
        /// <summary>Don't use cache.</summary>
        public bool NoCache { get; set; }
        /// <summary>Always pull newer versions of base images.</summary>
        public bool Pull { get; set; }
        /// <summary>Remove intermediate containers.</summary>
        public bool ForceRm { get; set; }
        /// <summary>Build arguments.</summary>
        public Dictionary<string, string> BuildArgs { get; set; } = new Dictionary<string, string>();
        /// <summary>Build in parallel.</summary>
        public bool Parallel { get; set; }
        /// <summary>Compress build context.</summary>
        public bool Compress { get; set; }
    }

    /// <summary>
    /// Configuration for compose pull operation.
    /// </summary>
    public class ComposePullConfig : ComposeFileConfig
    {
        /// <summary>Ignore pull failures.</summary>
        public bool IgnorePullFailures { get; set; }
        /// <summary>Pull in parallel.</summary>
        public bool Parallel { get; set; }
        /// <summary>Don't print progress information.</summary>
        public bool Quiet { get; set; }
        /// <summary>Also pull images for services with build config.</summary>
        public bool IncludeDeps { get; set; }
    }

    /// <summary>
    /// Configuration for compose exec operation.
    /// </summary>
    public class ComposeExecConfig : ComposeFileConfig
    {
        /// <summary>Service name.</summary>
        public string Service { get; set; }
        /// <summary>Command to execute.</summary>
        public string[] Command { get; set; }
        /// <summary>Detached mode.</summary>
        public bool Detach { get; set; }
        /// <summary>Give extended privileges.</summary>
        public bool Privileged { get; set; }
        /// <summary>Run as this user.</summary>
        public string User { get; set; }
        /// <summary>Allocate a pseudo-TTY.</summary>
        public bool Tty { get; set; } = true;
        /// <summary>Working directory inside container.</summary>
        public string WorkDir { get; set; }
        /// <summary>Index of container if scaled.</summary>
        public int? Index { get; set; }
    }

    /// <summary>
    /// Configuration for compose run operation.
    /// </summary>
    public class ComposeRunConfig : ComposeFileConfig
    {
        /// <summary>Service name.</summary>
        public string Service { get; set; }
        /// <summary>Command to run.</summary>
        public string[] Command { get; set; }
        /// <summary>Detached mode.</summary>
        public bool Detach { get; set; }
        /// <summary>Container name.</summary>
        public string Name { get; set; }
        /// <summary>Override entrypoint.</summary>
        public string Entrypoint { get; set; }
        /// <summary>Run as this user.</summary>
        public string User { get; set; }
        /// <summary>Working directory.</summary>
        public string WorkDir { get; set; }
        /// <summary>Don't start linked services.</summary>
        public bool NoDeps { get; set; }
        /// <summary>Remove container after run.</summary>
        public bool Rm { get; set; }
        /// <summary>Publish service ports.</summary>
        public bool ServicePorts { get; set; }
        /// <summary>Additional ports to expose.</summary>
        public List<string> Publish { get; set; } = new List<string>();
        /// <summary>Additional volumes.</summary>
        public List<string> Volumes { get; set; } = new List<string>();
        /// <summary>Allocate a pseudo-TTY.</summary>
        public bool Tty { get; set; }
    }

    /// <summary>
    /// Configuration for compose scale operation.
    /// </summary>
    public class ComposeScaleConfig : ComposeFileConfig
    {
        /// <summary>Service replicas (service=count).</summary>
        public Dictionary<string, int> Scale { get; set; } = new Dictionary<string, int>();
        /// <summary>Don't start linked services.</summary>
        public bool NoDeps { get; set; }
    }

    /// <summary>
    /// Configuration for compose cp operation.
    /// </summary>
    public class ComposeCopyConfig : ComposeFileConfig
    {
        /// <summary>Source path (container:path or local path).</summary>
        public string Source { get; set; }
        /// <summary>Destination path (container:path or local path).</summary>
        public string Destination { get; set; }
        /// <summary>Archive mode (copy all uid/gid info).</summary>
        public bool Archive { get; set; }
        /// <summary>Follow symbolic links.</summary>
        public bool FollowLinks { get; set; }
        /// <summary>Container index if scaled.</summary>
        public int? Index { get; set; }
    }

    /// <summary>
    /// Configuration for compose create operation.
    /// </summary>
    public class ComposeCreateConfig : ComposeFileConfig
    {
        /// <summary>Build images before creating.</summary>
        public bool Build { get; set; }
        /// <summary>Recreate containers even if unchanged.</summary>
        public bool ForceRecreate { get; set; }
        /// <summary>Don't recreate existing containers.</summary>
        public bool NoRecreate { get; set; }
        /// <summary>Don't build images.</summary>
        public bool NoBuild { get; set; }
        /// <summary>Pull images before creating.</summary>
        public string Pull { get; set; }
        /// <summary>Remove orphaned containers.</summary>
        public bool RemoveOrphans { get; set; }
    }

    /// <summary>
    /// Configuration for compose port operation.
    /// </summary>
    public class ComposePortConfig : ComposeFileConfig
    {
        /// <summary>Service name.</summary>
        public string Service { get; set; }
        /// <summary>Private port.</summary>
        public int PrivatePort { get; set; }
        /// <summary>Protocol (tcp or udp).</summary>
        public string Protocol { get; set; } = "tcp";
        /// <summary>Index of container if scaled.</summary>
        public int? Index { get; set; }
    }

    #endregion

    #region Result Types

    /// <summary>
    /// Result of a compose up operation.
    /// </summary>
    public class ComposeUpResult
    {
        /// <summary>List of started services.</summary>
        public List<string> Services { get; set; } = new List<string>();
        /// <summary>Project name.</summary>
        public string ProjectName { get; set; }
        /// <summary>Warnings from the operation.</summary>
        public List<string> Warnings { get; set; } = new List<string>();
    }

    /// <summary>
    /// Information about a compose service.
    /// </summary>
    public class ComposeServiceInfo
    {
        /// <summary>Service name (from compose file).</summary>
        [Newtonsoft.Json.JsonProperty("Service")]
        public string Name { get; set; }
        /// <summary>Current state (running, exited, etc.).</summary>
        public string State { get; set; }
        /// <summary>Status description (e.g., "Up 2 seconds").</summary>
        public string Status { get; set; }
        /// <summary>Health status.</summary>
        public string Health { get; set; }
        /// <summary>Container ID.</summary>
        [Newtonsoft.Json.JsonProperty("ID")]
        public string ContainerId { get; set; }
        /// <summary>Container name.</summary>
        [Newtonsoft.Json.JsonProperty("Name")]
        public string ContainerName { get; set; }
        /// <summary>Image being used.</summary>
        public string Image { get; set; }
        /// <summary>Command being run.</summary>
        public string Command { get; set; }
        /// <summary>Port mappings string.</summary>
        public string Ports { get; set; }
        /// <summary>Project name.</summary>
        public string Project { get; set; }
        /// <summary>Exit code (if stopped).</summary>
        public int ExitCode { get; set; }
        /// <summary>Created at timestamp.</summary>
        public string CreatedAt { get; set; }
        /// <summary>Running time description.</summary>
        public string RunningFor { get; set; }
        /// <summary>Publishers (structured port info).</summary>
        public List<ComposePublisher> Publishers { get; set; } = new List<ComposePublisher>();
    }

    /// <summary>
    /// Port publisher information from compose ps.
    /// </summary>
    public class ComposePublisher
    {
        /// <summary>URL/IP to bind to.</summary>
        public string URL { get; set; }
        /// <summary>Target port in container.</summary>
        public int TargetPort { get; set; }
        /// <summary>Published port on host.</summary>
        public int PublishedPort { get; set; }
        /// <summary>Protocol (tcp/udp).</summary>
        public string Protocol { get; set; }
    }

    /// <summary>
    /// Represents processes in a compose service.
    /// </summary>
    public class ComposeProcesses
    {
        /// <summary>Service name.</summary>
        public string Service { get; set; }
        /// <summary>Container ID.</summary>
        public string ContainerId { get; set; }
        /// <summary>Process information.</summary>
        public List<Dictionary<string, string>> Processes { get; set; } = new List<Dictionary<string, string>>();
    }

    /// <summary>
    /// Represents an image used by a compose service.
    /// </summary>
    public class ComposeImage
    {
        /// <summary>Container name.</summary>
        public string Container { get; set; }
        /// <summary>Repository.</summary>
        public string Repository { get; set; }
        /// <summary>Tag.</summary>
        public string Tag { get; set; }
        /// <summary>Image ID.</summary>
        public string ImageId { get; set; }
        /// <summary>Image size.</summary>
        public string Size { get; set; }
    }

    #endregion
}
