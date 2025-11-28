using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Model.Builders;
using FluentDocker.Model.Builders.FileBuilder;
using FluentDocker.Model.Common;

namespace FluentDocker.Builders
{
    /// <summary>
    /// Fluent builder for creating Dockerfile content programmatically.
    /// Can be used standalone to generate Dockerfile strings, or with ImageBuilder to build images.
    /// </summary>
    public sealed class DockerfileBuilder
    {
        private readonly FileBuilderConfig _config = new FileBuilderConfig();
        private readonly ImageBuilder _parent;
        private TemplateString _workingFolder;
        private string _lastContents;

        /// <summary>
        /// Creates a standalone DockerfileBuilder for generating Dockerfile content.
        /// </summary>
        public DockerfileBuilder()
        {
            _workingFolder = Path.Combine(Path.GetTempPath(), "fluentdockertest", Guid.NewGuid().ToString("N"));
        }

        /// <summary>
        /// Creates a DockerfileBuilder linked to an ImageBuilder.
        /// </summary>
        internal DockerfileBuilder(ImageBuilder parent)
        {
            _parent = parent;
            _workingFolder = Path.Combine(Path.GetTempPath(), "fluentdockertest", Guid.NewGuid().ToString("N"));
        }

        #region Build Operations

        /// <summary>
        /// Prepares the build by copying files and rendering the Dockerfile.
        /// </summary>
        /// <returns>Working directory path</returns>
        internal async Task<string> PrepareBuildAsync()
        {
            await CopyToWorkDirAsync(_workingFolder);
            RenderDockerfile(_workingFolder);
            return _workingFolder;
        }

        /// <summary>
        /// Builds the image using the parent ImageBuilder.
        /// </summary>
        /// <exception cref="FluentDockerException">If no ImageBuilder parent exists</exception>
        public Task<Services.IImageService> BuildAsync()
        {
            if (_parent == null)
                throw new FluentDockerException("No ImageBuilder was set as parent. Use Fd.DefineImage() to create one.");

            return _parent.ExecuteAsync(default);
        }

        /// <summary>
        /// Returns to the ImageBuilder for further configuration.
        /// </summary>
        public ImageBuilder ToImage()
        {
            if (_parent == null)
                throw new FluentDockerException("No ImageBuilder was set as parent. Use Fd.DefineImage() to create one.");

            return _parent;
        }

        /// <summary>
        /// Generates the Dockerfile as a string.
        /// </summary>
        /// <returns>Dockerfile content</returns>
        public async Task<string> ToDockerfileStringAsync()
        {
            await PrepareBuildAsync();
            return _lastContents;
        }

        /// <summary>
        /// Generates the Dockerfile as a string (synchronous).
        /// </summary>
        public string ToDockerfileString()
        {
            return Task.Run(() => ToDockerfileStringAsync()).GetAwaiter().GetResult();
        }

        #endregion

        #region Configuration

        /// <summary>
        /// Sets the working folder for file operations.
        /// </summary>
        public DockerfileBuilder WorkingFolder(string workingFolder)
        {
            _workingFolder = workingFolder;
            return this;
        }

        #endregion

        #region FROM Command

        /// <summary>
        /// Specifies the FROM command with just an image name.
        /// </summary>
        /// <param name="from">Image name and optional tag</param>
        public DockerfileBuilder UseParent(string from)
        {
            _config.Commands.Add(new FromCommand(from));
            return this;
        }

        /// <summary>
        /// Specifies the FROM command with full options.
        /// </summary>
        /// <param name="imageAndTag">Image name and optional tag</param>
        /// <param name="asName">Optional alias (for multi-stage builds)</param>
        /// <param name="platform">Optional platform (e.g., linux/amd64)</param>
        public DockerfileBuilder From(string imageAndTag, string asName = null, string platform = null)
        {
            _config.Commands.Add(new FromCommand(imageAndTag, asName, platform));
            return this;
        }

        #endregion

        #region Metadata Commands

        /// <summary>
        /// Adds a MAINTAINER instruction (deprecated, use LABEL instead).
        /// </summary>
        public DockerfileBuilder Maintainer(string maintainer)
        {
            _config.Commands.Add(new MaintainerCommand(maintainer));
            return this;
        }

        /// <summary>
        /// Adds LABEL instructions.
        /// </summary>
        /// <param name="nameValue">Name=value pairs</param>
        public DockerfileBuilder Label(params string[] nameValue)
        {
            _config.Commands.Add(new LabelCommand(nameValue.Select(x => (TemplateString)x).ToArray()));
            return this;
        }

        /// <summary>
        /// Adds ARG instructions for build arguments.
        /// </summary>
        /// <param name="name">Argument name</param>
        /// <param name="defaultValue">Optional default value</param>
        public DockerfileBuilder Arguments(string name, string defaultValue = null)
        {
            _config.Commands.Add(new ArgCommand(name, defaultValue));
            return this;
        }

        #endregion

        #region Build Commands

        /// <summary>
        /// Adds RUN instructions.
        /// </summary>
        /// <param name="commands">Commands to run</param>
        public DockerfileBuilder Run(params string[] commands)
        {
            foreach (var cmd in commands)
            {
                _config.Commands.Add(new RunCommand(cmd));
            }
            return this;
        }

        /// <summary>
        /// Adds a SHELL instruction.
        /// </summary>
        public DockerfileBuilder Shell(string command, params string[] args)
        {
            _config.Commands.Add(new ShellCommand(command, args));
            return this;
        }

        /// <summary>
        /// Adds an ADD instruction.
        /// </summary>
        public DockerfileBuilder Add(string source, string destination)
        {
            _config.Commands.Add(new AddCommand(source, destination));
            return this;
        }

        /// <summary>
        /// Adds a COPY instruction.
        /// </summary>
        /// <param name="source">Source path or URL</param>
        /// <param name="dest">Destination path in container</param>
        /// <param name="chownUserAndGroup">Optional --chown user:group</param>
        /// <param name="fromAlias">Optional --from=alias for multi-stage builds</param>
        public DockerfileBuilder Copy(string source, string dest,
            string chownUserAndGroup = null, string fromAlias = null)
        {
            var lc = source.ToLower();
            if (lc.StartsWith("http://") || lc.StartsWith("https://") ||
                lc.StartsWith("ftp://") || lc.StartsWith("ftps://"))
            {
                var uri = new Uri(source);
                var tmp = Path.Combine("___fluentdockerdl", Path.GetFileName(uri.LocalPath));
                _config.Commands.Add(new CopyURLCommand(uri, tmp, dest, chownUserAndGroup, fromAlias));
                return this;
            }

            _config.Commands.Add(new CopyCommand(source, dest, chownUserAndGroup, fromAlias));
            return this;
        }

        /// <summary>
        /// Sets the WORKDIR instruction.
        /// </summary>
        public DockerfileBuilder UseWorkDir(string workdir)
        {
            _config.Commands.Add(new WorkdirCommand(workdir));
            return this;
        }

        #endregion

        #region Runtime Commands

        /// <summary>
        /// Adds EXPOSE instructions for ports.
        /// </summary>
        public DockerfileBuilder ExposePorts(params int[] ports)
        {
            _config.Commands.Add(new ExposeCommand(ports));
            return this;
        }

        /// <summary>
        /// Adds ENV instructions.
        /// </summary>
        /// <param name="nameValue">Name=value pairs</param>
        public DockerfileBuilder Environment(params string[] nameValue)
        {
            _config.Commands.Add(new EnvCommand(nameValue.Select(x => (TemplateString)x).ToArray()));
            return this;
        }

        /// <summary>
        /// Adds a VOLUME instruction.
        /// </summary>
        public DockerfileBuilder Volume(params string[] mountpoints)
        {
            _config.Commands.Add(new VolumeCommand(mountpoints.Select(x => (TemplateString)x).ToArray()));
            return this;
        }

        /// <summary>
        /// Adds a USER instruction.
        /// </summary>
        public DockerfileBuilder User(string user, string group = null)
        {
            _config.Commands.Add(new UserCommand(user, group));
            return this;
        }

        /// <summary>
        /// Adds an ENTRYPOINT instruction.
        /// </summary>
        public DockerfileBuilder Entrypoint(string command, params string[] args)
        {
            _config.Commands.Add(new EntrypointCommand(command, args));
            return this;
        }

        /// <summary>
        /// Adds a CMD instruction.
        /// </summary>
        public DockerfileBuilder Command(string command, params string[] args)
        {
            _config.Commands.Add(new CmdCommand(command, args));
            return this;
        }

        /// <summary>
        /// Adds a HEALTHCHECK instruction.
        /// </summary>
        /// <param name="cmd">Health check command</param>
        /// <param name="interval">Check interval (e.g., "30s")</param>
        /// <param name="timeout">Check timeout (e.g., "30s")</param>
        /// <param name="startPeriod">Start period before checks begin</param>
        /// <param name="retries">Number of retries before marking unhealthy</param>
        public DockerfileBuilder WithHealthCheck(string cmd, string interval = null, 
            string timeout = null, string startPeriod = null, int retries = 3)
        {
            _config.Commands.Add(new HealthCheckCommand(cmd, interval, timeout, startPeriod, retries));
            return this;
        }

        #endregion

        #region From Existing Dockerfile

        /// <summary>
        /// Uses an existing Dockerfile from a file path.
        /// </summary>
        public DockerfileBuilder FromFile(string file)
        {
            _config.UseFile = file;
            return this;
        }

        /// <summary>
        /// Uses a Dockerfile content string.
        /// </summary>
        public DockerfileBuilder FromString(string dockerFileAsString)
        {
            _config.DockerFileString = dockerFileAsString;
            return this;
        }

        #endregion

        #region Private Methods

        private async Task CopyToWorkDirAsync(string workingFolder)
        {
            if (!Directory.Exists(workingFolder))
                Directory.CreateDirectory(workingFolder);

            // Copy all files from copy arguments
            foreach (var cp in _config.Commands.Where(x => x is CopyCommand).Cast<CopyCommand>())
            {
                if (cp is CopyURLCommand urlCmd)
                {
                    var wdlp = Path.Combine(workingFolder, cp.From);
                    var dd = Path.GetDirectoryName(wdlp);

                    if (!string.IsNullOrEmpty(dd) && !Directory.Exists(dd))
                        Directory.CreateDirectory(dd);

                    await DownloadFileAsync(urlCmd.FromURL, wdlp);
                    continue;
                }

                // Standard CopyCommand
                if (!File.Exists(cp.From))
                    continue;

                var wp = Path.Combine(workingFolder, cp.From);
                var wdp = Path.GetDirectoryName(wp);
                if (!string.IsNullOrEmpty(wdp) && !Directory.Exists(wdp))
                    Directory.CreateDirectory(wdp);

                File.Copy(cp.From, wp, true);
            }

            foreach (var command in _config.Commands.Where(x => x is AddCommand).Cast<AddCommand>())
            {
                var wff = Path.Combine(workingFolder, command.Source);
                if (File.Exists(wff) || Directory.Exists(wff))
                    continue;

                // Copy to working folder
                command.Source = CopyToWorkDir(command.Source, workingFolder);
            }
        }

        private static string CopyToWorkDir(string source, string workingFolder)
        {
            if (!File.Exists(source) && !Directory.Exists(source))
                return source;

            var dest = Path.Combine(workingFolder, Path.GetFileName(source));
            
            if (File.Exists(source))
            {
                File.Copy(source, dest, true);
            }
            else if (Directory.Exists(source))
            {
                DirectoryHelper.CopyFilesRecursively(new DirectoryInfo(source), new DirectoryInfo(dest));
            }

            return Path.GetFileName(source);
        }

        private static async Task DownloadFileAsync(Uri url, string destinationPath)
        {
            using var client = new HttpClient();
            var response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();
            
            var content = await response.Content.ReadAsByteArrayAsync();
            await File.WriteAllBytesAsync(destinationPath, content);
        }

        private void RenderDockerfile(string workingFolder)
        {
            if (!Directory.Exists(workingFolder))
                Directory.CreateDirectory(workingFolder);

            var dockerFile = Path.Combine(workingFolder, "Dockerfile");

            var contents = !string.IsNullOrEmpty(_config.UseFile?.Rendered)
                ? File.ReadAllText(_config.UseFile)
                : ResolveOrBuildString();

            File.WriteAllText(dockerFile, contents);
            _lastContents = contents;
        }

        private string ResolveOrBuildString()
        {
            return !string.IsNullOrWhiteSpace(_config.DockerFileString)
                ? _config.DockerFileString
                : _config.ToString();
        }

        #endregion
    }
}

