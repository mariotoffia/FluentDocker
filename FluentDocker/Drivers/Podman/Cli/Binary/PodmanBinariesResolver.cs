using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentDocker.Common;
using FluentDocker.Model.Common;
using static FluentDocker.Common.FdOs;

namespace FluentDocker.Drivers.Podman.Cli.Binary
{
    /// <summary>
    /// Resolves the available Podman binaries on the local machine.
    /// </summary>
    public sealed class PodmanBinariesResolver : IPodmanBinaryResolver
    {
        private readonly PodmanBinaryConfiguration _configuration;

        /// <summary>
        /// Creates a new resolver using the provided configuration.
        /// </summary>
        public PodmanBinariesResolver(PodmanBinaryConfiguration configuration)
        {
            _configuration = configuration ?? new PodmanBinaryConfiguration();

            Binaries = ResolveFromPaths(
                _configuration.Sudo,
                _configuration.SudoPassword,
                _configuration.SearchPaths).ToArray();

            MainPodmanClient = Binaries.FirstOrDefault(x => x.Type == PodmanBinaryType.PodmanClient);
            PodmanRemote = Binaries.FirstOrDefault(x => x.Type == PodmanBinaryType.PodmanRemote);

            if (MainPodmanClient == null)
            {
                Logger.Log("Failed to find podman client binary - please add it to your path");
                throw new FluentDockerException(
                    "Failed to find podman client binary - please add it to your path");
            }
        }

        /// <summary>
        /// Creates a new resolver with explicit sudo settings and paths.
        /// </summary>
        public PodmanBinariesResolver(SudoMechanism sudo, string password, params string[] paths)
            : this(new PodmanBinaryConfiguration
            {
                Sudo = sudo,
                SudoPassword = password,
                SearchPaths = paths?.Length > 0 ? paths : null
            })
        {
        }

        /// <inheritdoc />
        public PodmanBinary[] Binaries { get; }

        /// <inheritdoc />
        public PodmanBinary MainPodmanClient { get; }

        /// <inheritdoc />
        public PodmanBinary PodmanRemote { get; }

        /// <inheritdoc />
        public PodmanBinary Resolve(string binary)
        {
            var type = PodmanBinary.Translate(binary);

            var resolved = type switch
            {
                PodmanBinaryType.PodmanClient => MainPodmanClient,
                PodmanBinaryType.PodmanRemote => PodmanRemote,
                _ => throw new FluentDockerException($"Cannot resolve unknown binary {binary}"),
            };

            if (resolved == null)
            {
                throw new FluentDockerException(
                    $"Could not resolve binary {binary} - is it installed on the local system?");
            }

            return resolved;
        }

        /// <inheritdoc />
        public string ResolveBinaryPath(string podmanCommand)
        {
            var binary = Resolve(podmanCommand);

            if (IsWindows() || binary.Sudo == SudoMechanism.None)
                return binary.FqPath;

            var cmd = binary.Sudo == SudoMechanism.NoPassword
                ? $"sudo {binary.FqPath}"
                : $"echo {binary.SudoPassword} | sudo -S {binary.FqPath}";

            if (string.IsNullOrEmpty(cmd))
            {
                throw new FluentDockerException(
                    $"Could not find {podmanCommand} - make sure it is on your path.");
            }

            return cmd;
        }

        private static IEnumerable<PodmanBinary> ResolveFromPaths(
            SudoMechanism sudo, string password, params string[] paths)
        {
            var isWindows = IsWindows();
            if (paths == null || paths.Length == 0)
            {
                var envpaths = Environment.GetEnvironmentVariable("PATH")
                    ?.Split(isWindows ? ';' : ':');
                paths = envpaths ?? Array.Empty<string>();
            }

            if (paths == null || paths.Length == 0)
                return Array.Empty<PodmanBinary>();

            var list = new List<PodmanBinary>();
            foreach (var path in paths)
            {
                try
                {
                    if (!Directory.Exists(path))
                        continue;

                    if (isWindows)
                    {
                        list.AddRange(from file in Directory.GetFiles(path, "podman*.*")
                            let f = Path.GetFileName(file.ToLower())
                            where f != null && (f.Equals("podman.exe") || f.Equals("podman-remote.exe"))
                            select new PodmanBinary(path, f, sudo, password));
                        continue;
                    }

                    list.AddRange(from file in Directory.GetFiles(path, "podman*")
                        let f = Path.GetFileName(file)
                        let f2 = f.ToLower()
                        where f2.Equals("podman") || f2.Equals("podman-remote")
                        select new PodmanBinary(path, f, sudo, password));
                }
                catch (Exception e)
                {
                    Logger.Log("Failed to get podman binary from path: " + path +
                               Environment.NewLine + e);
                }
            }

            return list;
        }
    }
}
