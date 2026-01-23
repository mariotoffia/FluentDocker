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

            MainDockerClient = Binaries.FirstOrDefault(x => !x.IsToolbox && x.Type == DockerBinaryType.DockerClient);
            MainDockerCompose = Binaries.FirstOrDefault(x => !x.IsToolbox && x.Type == DockerBinaryType.Compose);
            MainDockerComposeV2 = CheckComposeV2(_configuration.Sudo, _configuration.SudoPassword);
            MainDockerMachine = Binaries.FirstOrDefault(x => !x.IsToolbox && x.Type == DockerBinaryType.Machine);
            MainDockerCli = Binaries.FirstOrDefault(x => !x.IsToolbox && x.Type == DockerBinaryType.Cli);
            HasToolbox = Binaries.Any(x => x.IsToolbox);

            if (MainDockerClient == null)
            {
                Logger.Log("Failed to find docker client binary - please add it to your path");
                throw new FluentDockerException("Failed to find docker client binary - please add it to your path");
            }

            if (MainDockerCompose == null && MainDockerComposeV2 == null)
            {
                Logger.Log("Failed to find docker-compose client binary (neither V1 nor V2) - please add it to your path");
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
        public DockerBinary MainDockerCompose { get; }

        /// <inheritdoc />
        public DockerBinary MainDockerComposeV2 { get; }

        /// <inheritdoc />
        public DockerBinary MainDockerMachine { get; }

        /// <inheritdoc />
        public DockerBinary MainDockerCli { get; }

        /// <inheritdoc />
        public bool IsToolbox => MainDockerClient?.IsToolbox ?? false;

        /// <inheritdoc />
        public bool IsDockerMachineAvailable => MainDockerMachine != null;

        /// <inheritdoc />
        public bool IsDockerComposeAvailable => MainDockerCompose != null || MainDockerComposeV2 != null;

        /// <inheritdoc />
        public bool IsDockerComposeV2Available => MainDockerComposeV2 != null;

        /// <inheritdoc />
        public bool HasToolbox { get; }

        /// <inheritdoc />
        public DockerBinary Resolve(string binary, bool preferMachine = false)
        {
            var type = DockerBinary.Translate(binary);
            if (preferMachine)
            {
                var m = Binaries.FirstOrDefault(x => x.IsToolbox && x.Type == type);
                if (m != null)
                {
                    return m;
                }
            }

            var resolved = type switch
            {
                DockerBinaryType.Compose => MainDockerComposeV2 ?? MainDockerCompose,
                DockerBinaryType.DockerClient => MainDockerClient,
                DockerBinaryType.Machine => MainDockerMachine,
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
        public string ResolveBinaryPath(string dockerCommand, bool preferMachine = false)
        {
            var binary = Resolve(dockerCommand, preferMachine);

            // Special handling for Docker Compose V2
            if (binary.Type == DockerBinaryType.ComposeV2 &&
                dockerCommand.Equals("docker-compose", StringComparison.OrdinalIgnoreCase))
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
                if (!string.IsNullOrEmpty(dockerCommand) &&
                    dockerCommand.Equals("docker-machine", StringComparison.OrdinalIgnoreCase))
                {
                    throw new FluentDockerException(
                        $"Could not find {dockerCommand} - make sure it is on your path. " +
                        "From Docker 2.2.0 you have to separately install it via https://github.com/docker/machine/releases");
                }

                throw new FluentDockerException($"Could not find {dockerCommand} - make sure it is on your path.");
            }

            return cmd;
        }

        private static IEnumerable<DockerBinary> ResolveFromPaths(SudoMechanism sudo, string password, params string[] paths)
        {
            var isWindows = IsWindows();
            if (paths == null || paths.Length == 0)
            {
                var complete = new List<string>();
                var toolboxpath = Environment.GetEnvironmentVariable("DOCKER_TOOLBOX_INSTALL_PATH");
                var envpaths = Environment.GetEnvironmentVariable("PATH")?.Split(isWindows ? ';' : ':');

                if (envpaths != null)
                    complete.AddRange(envpaths);
                if (toolboxpath != null)
                    complete.Add(toolboxpath);

                paths = complete.ToArray();
            }

            if (paths == null)
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
                            where f != null && (f.Equals("docker.exe") || f.Equals("docker-compose.exe") ||
                                                f.Equals("docker-machine.exe"))
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
                        where f2.Equals("docker") || f2.Equals("docker-compose") || f2.Equals("docker-machine")
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
