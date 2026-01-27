using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentDocker.Common;
using FluentDocker.Model.Common;
using FluentDocker.Executors;
using static FluentDocker.Common.FdOs;

namespace FluentDocker.Drivers.Docker.Cli.Binary
{
    /// <summary>
    /// Resolves the available Docker binaries on the local machine.
    /// Implements IBinaryResolver to provide binary resolution for the CLI driver.
    /// </summary>
    /// <remarks>
    /// v3.0 Note: Docker Machine and Docker Toolbox support has been removed.
    /// Only Docker CLI and Docker Compose V2 (docker compose) are supported.
    /// </remarks>
    public sealed class DockerBinariesResolver : IBinaryResolver
    {
        private readonly BinaryConfiguration _configuration;

        /// <summary>
        /// Creates a new resolver using the provided configuration.
        /// </summary>
        /// <param name="configuration">The binary configuration.</param>
        public DockerBinariesResolver(BinaryConfiguration configuration)
        {
            _configuration = configuration ?? new BinaryConfiguration();

            Binaries = ResolveFromPaths(
                _configuration.Sudo,
                _configuration.SudoPassword,
                _configuration.SearchPaths).ToArray();

            MainDockerClient = Binaries.FirstOrDefault(x => x.Type == DockerBinaryType.DockerClient);
            MainDockerComposeV2 = CheckComposeV2(_configuration.Sudo, _configuration.SudoPassword);
            MainDockerCli = Binaries.FirstOrDefault(x => x.Type == DockerBinaryType.Cli);

            if (MainDockerClient == null)
            {
                Logger.Log("Failed to find docker client binary - please add it to your path");
                throw new FluentDockerException("Failed to find docker client binary - please add it to your path");
            }

            if (MainDockerComposeV2 == null)
            {
                Logger.Log("Docker Compose V2 (docker compose) is not available - compose features will not work");
            }
        }

        /// <summary>
        /// Creates a new resolver with explicit sudo settings and paths.
        /// </summary>
        /// <param name="sudo">The sudo mechanism to use.</param>
        /// <param name="password">The sudo password (if required).</param>
        /// <param name="paths">Custom search paths (uses PATH if empty).</param>
        public DockerBinariesResolver(SudoMechanism sudo, string password, params string[] paths)
            : this(new BinaryConfiguration
            {
                Sudo = sudo,
                SudoPassword = password,
                SearchPaths = paths?.Length > 0 ? paths : null
            })
        {
        }

        /// <inheritdoc />
        public DockerBinary[] Binaries { get; }

        /// <inheritdoc />
        public DockerBinary MainDockerClient { get; }

        /// <inheritdoc />
        public DockerBinary MainDockerComposeV2 { get; }

        /// <inheritdoc />
        public DockerBinary MainDockerCli { get; }

        /// <inheritdoc />
        public bool IsDockerComposeV2Available => MainDockerComposeV2 != null;

        /// <inheritdoc />
        public DockerBinary Resolve(string binary)
        {
            var type = DockerBinary.Translate(binary);

            var resolved = type switch
            {
                DockerBinaryType.ComposeV2 => MainDockerComposeV2,
                DockerBinaryType.DockerClient => MainDockerClient,
                DockerBinaryType.Cli => MainDockerCli,
                _ => throw new FluentDockerException($"Cannot resolve unknown binary {binary}"),
            };

            if (resolved == null)
            {
                throw new FluentDockerException($"Could not resolve binary {binary} - is it installed on the local system?");
            }

            return resolved;
        }

        /// <inheritdoc />
        public string ResolveBinaryPath(string dockerCommand)
        {
            var binary = Resolve(dockerCommand);

            // Special handling for Docker Compose V2
            if (binary.Type == DockerBinaryType.ComposeV2 &&
                (dockerCommand.Equals("docker-compose", StringComparison.OrdinalIgnoreCase) ||
                 dockerCommand.Equals("compose", StringComparison.OrdinalIgnoreCase)))
            {
                // For V2, we need to return 'docker compose' instead of 'docker-compose'
                if (IsWindows() || binary.Sudo == SudoMechanism.None)
                    return $"{binary.FqPath} compose";

                return binary.Sudo == SudoMechanism.NoPassword
                    ? $"sudo {binary.FqPath} compose"
                    : $"echo {binary.SudoPassword} | sudo -S {binary.FqPath} compose";
            }

            if (IsWindows() || binary.Sudo == SudoMechanism.None)
                return binary.FqPath;

            var cmd = binary.Sudo == SudoMechanism.NoPassword
                ? $"sudo {binary.FqPath}"
                : $"echo {binary.SudoPassword} | sudo -S {binary.FqPath}";

            if (string.IsNullOrEmpty(cmd))
            {
                throw new FluentDockerException($"Could not find {dockerCommand} - make sure it is on your path.");
            }

            return cmd;
        }

        private static IEnumerable<DockerBinary> ResolveFromPaths(SudoMechanism sudo, string password, params string[] paths)
        {
            var isWindows = IsWindows();
            if (paths == null || paths.Length == 0)
            {
                var envpaths = Environment.GetEnvironmentVariable("PATH")?.Split(isWindows ? ';' : ':');
                paths = envpaths ?? Array.Empty<string>();
            }

            if (paths == null || paths.Length == 0)
                return Array.Empty<DockerBinary>();

            var list = new List<DockerBinary>();
            foreach (var path in paths)
            {
                try
                {
                    if (!Directory.Exists(path))
                    {
                        continue;
                    }

                    if (isWindows)
                    {
                        list.AddRange(from file in Directory.GetFiles(path, "docker*.*")
                            let f = Path.GetFileName(file.ToLower())
                            where f != null && f.Equals("docker.exe")
                            select new DockerBinary(path, f, sudo, password));

                        var dockercli = Path.GetFullPath(Path.Combine(path, "..\\.."));
                        if (File.Exists(Path.Combine(dockercli, "dockercli.exe")))
                        {
                            list.Add(new DockerBinary(dockercli, "dockercli.exe", sudo, password));
                        }

                        continue;
                    }

                    list.AddRange(from file in Directory.GetFiles(path, "docker*")
                        let f = Path.GetFileName(file)
                        let f2 = f.ToLower()
                        where f2.Equals("docker")
                        select new DockerBinary(path, f, sudo, password));
                }
                catch (Exception e)
                {
                    Logger.Log("Failed to get docker binary from path: " + path + Environment.NewLine + e);
                }
            }

            return list;
        }

        private DockerBinary CheckComposeV2(SudoMechanism sudo, string password)
        {
            if (MainDockerClient == null)
                return null;

            try
            {
                var result = new ProcessExecutor<Executors.Parsers.StringListResponseParser, IList<string>>(
                    MainDockerClient.FqPath,
                    "compose version").Execute();

                if (result.Success)
                {
                    return new DockerBinary(
                        Path.GetDirectoryName(MainDockerClient.FqPath),
                        Path.GetFileName(MainDockerClient.FqPath),
                        sudo,
                        password,
                        DockerBinaryType.ComposeV2);
                }
            }
            catch
            {
                Logger.Log("Docker Compose V2 plugin is not available");
            }

            return null;
        }
    }
}
