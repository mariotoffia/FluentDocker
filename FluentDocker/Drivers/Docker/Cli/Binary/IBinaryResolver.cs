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
    /// Docker CLI binary (dockercli).
    /// </summary>
    Cli = 4,
    /// <summary>
    /// Docker Compose - a DockerClient that supports the 'compose' subcommand.
    /// </summary>
    Compose = 5
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
    /// Gets the main Docker Compose binary (docker compose subcommand).
    /// </summary>
    DockerBinary MainDockerCompose { get; }

    /// <summary>
    /// Gets the main Docker CLI binary.
    /// </summary>
    DockerBinary MainDockerCli { get; }

    /// <summary>
    /// Gets whether Docker Compose is available.
    /// </summary>
    bool IsDockerComposeAvailable { get; }

    /// <summary>
    /// Resolves a Docker binary by command name.
    /// </summary>
    /// <param name="binary">The binary name (e.g., "docker").</param>
    /// <returns>The resolved DockerBinary.</returns>
    DockerBinary Resolve(string binary);

    /// <summary>
    /// Resolves the full path to a Docker binary, including sudo prefix if configured.
    /// </summary>
    /// <param name="dockerCommand">The Docker command name.</param>
    /// <returns>The command string ready for execution.</returns>
    string ResolveBinaryPath(string dockerCommand);
  }
}
