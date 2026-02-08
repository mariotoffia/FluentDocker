using System;
using FluentDocker.Model.Common;

namespace FluentDocker.Drivers.Podman.Cli.Binary
{
    /// <summary>
    /// Represents a resolved Podman binary on the local machine.
    /// </summary>
    public sealed class PodmanBinary
    {
        /// <summary>
        /// Creates a new PodmanBinary instance.
        /// </summary>
        /// <param name="path">The directory containing the binary.</param>
        /// <param name="binary">The binary filename.</param>
        /// <param name="sudo">The sudo mechanism to use.</param>
        /// <param name="password">The sudo password (if required).</param>
        public PodmanBinary(string path, string binary, SudoMechanism sudo, string password)
        {
            Path = path;
            Binary = binary.ToLower();
            Type = Translate(binary);
            Sudo = sudo;
            SudoPassword = password;
        }

        /// <summary>
        /// Creates a new PodmanBinary instance with an explicit type.
        /// </summary>
        public PodmanBinary(string path, string binary, SudoMechanism sudo, string password, PodmanBinaryType type)
        {
            Path = path;
            Binary = binary.ToLower();
            Type = type;
            Sudo = sudo;
            SudoPassword = password;
        }

        /// <summary>
        /// Translates a binary name to its PodmanBinaryType.
        /// </summary>
        /// <exception cref="ArgumentException">Thrown when the binary name is not recognized.</exception>
        public static PodmanBinaryType Translate(string binary)
        {
            return binary.ToLower() switch
            {
                "podman" or "podman.exe" => PodmanBinaryType.PodmanClient,
                "podman-remote" or "podman-remote.exe" => PodmanBinaryType.PodmanRemote,
                _ => throw new ArgumentException(
                    $"Cannot determine the podman type for binary '{binary}'.", nameof(binary))
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
        public PodmanBinaryType Type { get; }

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
