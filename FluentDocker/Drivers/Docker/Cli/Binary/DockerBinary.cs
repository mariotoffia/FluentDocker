using System;
using FluentDocker.Model.Common;

namespace FluentDocker.Drivers.Docker.Cli.Binary
{
    /// <summary>
    /// Represents a resolved Docker binary on the local machine.
    /// </summary>
    public sealed class DockerBinary
    {
        /// <summary>
        /// Creates a new DockerBinary instance.
        /// </summary>
        /// <param name="path">The directory containing the binary.</param>
        /// <param name="binary">The binary filename.</param>
        /// <param name="sudo">The sudo mechanism to use.</param>
        /// <param name="password">The sudo password (if required).</param>
        public DockerBinary(string path, string binary, SudoMechanism sudo, string password)
        {
            Path = path;
            Binary = binary.ToLower();
            Type = Translate(binary);
            Sudo = sudo;
            SudoPassword = password;

            var isToolbox = Environment.GetEnvironmentVariable("DOCKER_TOOLBOX_INSTALL_PATH")?.Equals(Path);
            IsToolbox = isToolbox ?? false;
        }

        /// <summary>
        /// Creates a new DockerBinary instance with an explicit type.
        /// </summary>
        /// <param name="path">The directory containing the binary.</param>
        /// <param name="binary">The binary filename.</param>
        /// <param name="sudo">The sudo mechanism to use.</param>
        /// <param name="password">The sudo password (if required).</param>
        /// <param name="type">The explicit binary type.</param>
        public DockerBinary(string path, string binary, SudoMechanism sudo, string password, DockerBinaryType type)
        {
            Path = path;
            Binary = binary.ToLower();
            Type = type;
            Sudo = sudo;
            SudoPassword = password;

            var isToolbox = Environment.GetEnvironmentVariable("DOCKER_TOOLBOX_INSTALL_PATH")?.Equals(Path);
            IsToolbox = isToolbox ?? false;
        }

        /// <summary>
        /// Translates a binary name to its DockerBinaryType.
        /// </summary>
        /// <param name="binary">The binary name.</param>
        /// <returns>The corresponding DockerBinaryType.</returns>
        /// <exception cref="ArgumentException">Thrown when the binary name is not recognized.</exception>
        public static DockerBinaryType Translate(string binary)
        {
            return binary.ToLower() switch
            {
                "docker" or "docker.exe" => DockerBinaryType.DockerClient,
                "docker-machine" or "docker-machine.exe" => DockerBinaryType.Machine,
                "docker-compose" or "docker-compose.exe" => DockerBinaryType.Compose,
                "dockercli" or "dockercli.exe" => DockerBinaryType.Cli,
                _ => throw new ArgumentException($"Cannot determine the docker type on binary {binary}", nameof(binary))
            };
        }

        /// <summary>
        /// Gets the fully qualified path to the binary.
        /// </summary>
        public string FqPath => System.IO.Path.Combine(Path, Binary);

        /// <summary>
        /// Gets the directory containing the binary.
        /// </summary>
        public string Path { get; }

        /// <summary>
        /// Gets the binary filename.
        /// </summary>
        public string Binary { get; }

        /// <summary>
        /// Gets the binary type.
        /// </summary>
        public DockerBinaryType Type { get; }

        /// <summary>
        /// Gets whether this binary is from Docker Toolbox.
        /// </summary>
        public bool IsToolbox { get; }

        /// <summary>
        /// Gets the sudo mechanism for this binary.
        /// </summary>
        public SudoMechanism Sudo { get; }

        /// <summary>
        /// Gets the sudo password (if configured).
        /// </summary>
        public string SudoPassword { get; }
    }
}
