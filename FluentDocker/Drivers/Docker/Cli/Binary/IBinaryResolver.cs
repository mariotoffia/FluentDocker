namespace FluentDocker.Drivers.Docker.Cli.Binary
{
    /// <summary>
    /// Represents the type of Docker binary.
    /// </summary>
    public enum DockerBinaryType
    {
        /// <summary>
        /// The main Docker client binary (docker/docker.exe).
        /// </summary>
        DockerClient = 1,
        /// <summary>
        /// Docker Machine binary (docker-machine).
        /// </summary>
        Machine = 2,
        /// <summary>
        /// Docker Compose V1 binary (docker-compose).
        /// </summary>
        Compose = 3,
        /// <summary>
        /// Docker CLI binary (dockercli).
        /// </summary>
        Cli = 4,
        /// <summary>
        /// Docker Compose V2 - a DockerClient that supports the 'compose' subcommand.
        /// Used to distinguish between legacy 'docker-compose' and new 'docker compose' command.
        /// </summary>
        ComposeV2 = 5
    }

    /// <summary>
    /// Interface for resolving Docker binaries on the local machine.
    /// </summary>
    public interface IBinaryResolver
    {
        /// <summary>
        /// Gets all resolved Docker binaries.
        /// </summary>
        DockerBinary[] Binaries { get; }

        /// <summary>
        /// Gets the main Docker client binary.
        /// </summary>
        DockerBinary MainDockerClient { get; }

        /// <summary>
        /// Gets the main Docker Compose V1 binary (docker-compose).
        /// </summary>
        DockerBinary MainDockerCompose { get; }

        /// <summary>
        /// Gets the main Docker Compose V2 binary (docker compose subcommand).
        /// </summary>
        DockerBinary MainDockerComposeV2 { get; }

        /// <summary>
        /// Gets the main Docker Machine binary.
        /// </summary>
        DockerBinary MainDockerMachine { get; }

        /// <summary>
        /// Gets the main Docker CLI binary.
        /// </summary>
        DockerBinary MainDockerCli { get; }

        /// <summary>
        /// Gets whether the Docker client is from Docker Toolbox.
        /// </summary>
        bool IsToolbox { get; }

        /// <summary>
        /// Gets whether Docker Machine is available.
        /// </summary>
        bool IsDockerMachineAvailable { get; }

        /// <summary>
        /// Gets whether Docker Compose (V1 or V2) is available.
        /// </summary>
        bool IsDockerComposeAvailable { get; }

        /// <summary>
        /// Gets whether Docker Compose V2 is available.
        /// </summary>
        bool IsDockerComposeV2Available { get; }

        /// <summary>
        /// Gets whether Docker Toolbox binaries are present.
        /// </summary>
        bool HasToolbox { get; }

        /// <summary>
        /// Resolves a Docker binary by command name.
        /// </summary>
        /// <param name="binary">The binary name (e.g., "docker", "docker-compose").</param>
        /// <param name="preferMachine">If true, prefer Docker Toolbox binaries.</param>
        /// <returns>The resolved DockerBinary.</returns>
        DockerBinary Resolve(string binary, bool preferMachine = false);

        /// <summary>
        /// Resolves the full path to a Docker binary, including sudo prefix if configured.
        /// </summary>
        /// <param name="dockerCommand">The Docker command name.</param>
        /// <param name="preferMachine">If true, prefer Docker Toolbox binaries.</param>
        /// <returns>The command string ready for execution.</returns>
        string ResolveBinaryPath(string dockerCommand, bool preferMachine = false);
    }
}
