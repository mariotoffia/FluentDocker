using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
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
  public sealed partial class DockerfileBuilder
  {
    private readonly FileBuilderConfig _config = new();
    private readonly ImageBuilder _parent;
    private TemplateString _workingFolder;
    private TemplateString _buildContext;
    private string _lastContents;
    private string _preparedDockerfileName;
    private readonly Dictionary<AddCommand, TemplateString> _addSourceOverrides = [];
    private readonly Dictionary<CopyCommand, string> _copySourceOverrides = [];
    private bool _ownsWorkingFolder = true;

    /// <summary>
    /// When an in-place build context is used (see <see cref="WithBuildContext"/>), this is
    /// the name of the existing Dockerfile relative to the build context, suitable for the
    /// driver's <c>--file</c> argument. <c>null</c> for the default (rendered) build path.
    /// </summary>
    internal string PreparedDockerfileName => _preparedDockerfileName;

    internal bool HasFromInstruction =>
        _config.Commands.Any(x => x is FromCommand) ||
        ContainsFromInstruction(_config.DockerFileString) ||
        ContainsFromInstruction(_lastContents);

    private bool IsInPlaceBuild =>
        _buildContext != null && !string.IsNullOrEmpty(_config.UseFile?.Rendered);

    private static bool ContainsFromInstruction(string contents) =>
        !string.IsNullOrWhiteSpace(contents) &&
        contents.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Any(line => line.TrimStart().StartsWith("FROM ", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Creates a standalone DockerfileBuilder for generating Dockerfile content.
    /// </summary>
    public DockerfileBuilder() => _workingFolder = Path.Combine(Path.GetTempPath(), "fluentdockertest", Guid.NewGuid().ToString("N"));

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
    internal async Task<string> PrepareBuildAsync(
        bool strictCopySources = false, CancellationToken cancellationToken = default)
    {
      if (IsInPlaceBuild)
        return await PrepareInPlaceBuildAsync(cancellationToken).ConfigureAwait(false);

      await CopyToWorkDirAsync(_workingFolder, strictCopySources, cancellationToken).ConfigureAwait(false);
      await RenderDockerfileAsync(_workingFolder, cancellationToken).ConfigureAwait(false);
      return _workingFolder;
    }

    /// <summary>
    /// Prepares an in-place build: uses an existing Dockerfile within the caller-provided
    /// build context without copying or rendering anything, so no generated Dockerfile is
    /// left behind (issue #280).
    /// </summary>
    private async Task<string> PrepareInPlaceBuildAsync(CancellationToken cancellationToken)
    {
      var context = _buildContext.Rendered;
      if (string.IsNullOrEmpty(context) || !Directory.Exists(context))
        throw new FluentDockerException(
            $"WithBuildContext path '{context}' does not exist.");

      var dockerfilePath = _config.UseFile.Rendered;
      if (string.IsNullOrEmpty(dockerfilePath) || !File.Exists(dockerfilePath))
        throw new FluentDockerException(
            $"FromFile path '{dockerfilePath}' does not exist.");

      // The Dockerfile must live inside the build context so docker/podman (and the
      // Engine API context tarball) can reference it via --file relative to the context.
      var fullContext = Path.GetFullPath(context);
      var fullDockerfile = Path.GetFullPath(dockerfilePath);
      var relative = Path.GetRelativePath(fullContext, fullDockerfile);
      if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
        throw new FluentDockerException(
            $"The Dockerfile '{fullDockerfile}' must reside inside the build context '{fullContext}'.");

      _preparedDockerfileName = relative.Replace('\\', '/');
      _lastContents = await File.ReadAllTextAsync(fullDockerfile, cancellationToken).ConfigureAwait(false);
      return fullContext;
    }

    /// <summary>
    /// Builds the image using the parent ImageBuilder.
    /// </summary>
    /// <exception cref="FluentDockerException">If no ImageBuilder parent exists</exception>
    public Task<Services.IImageService> BuildAsync(CancellationToken cancellationToken = default)
    {
      if (_parent == null)
        throw new FluentDockerException("No ImageBuilder was set as parent. Use new ImageBuilder() to create one.");

      return _parent.ExecuteAsync(cancellationToken);
    }

    /// <summary>
    /// Returns to the ImageBuilder for further configuration.
    /// </summary>
    public ImageBuilder ToImage()
    {
      if (_parent == null)
        throw new FluentDockerException("No ImageBuilder was set as parent. Use new ImageBuilder() to create one.");

      return _parent;
    }

    /// <summary>
    /// Generates the Dockerfile as a string.
    /// </summary>
    /// <remarks>URL COPY sources are downloaded to the build context before rendering.</remarks>
    /// <returns>Dockerfile content</returns>
    public async Task<string> ToDockerfileStringAsync(CancellationToken cancellationToken = default)
    {
      try
      {
        await PrepareBuildAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        return _lastContents;
      }
      finally
      {
        // ponytail: string-gen still stages files/downloads URLs then cleans up; fully filesystem-free rendering deferred — the COPY-basename rewrite depends on staging.
        DeleteOwnedWorkingFolder();
      }
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
      _ownsWorkingFolder = false;
      return this;
    }

    /// <summary>
    /// Builds an existing Dockerfile (set via <see cref="FromFile"/>) in place, using
    /// <paramref name="contextPath"/> as the build context. The existing Dockerfile is
    /// passed to the engine via <c>--file</c> instead of being copied or rendered, so no
    /// generated <c>Dockerfile</c> is left in your working directory (issue #280).
    /// </summary>
    /// <param name="contextPath">
    /// The build context directory. The Dockerfile passed to <see cref="FromFile"/> must
    /// reside inside this directory (it may be in a sub-directory).
    /// </param>
    public DockerfileBuilder WithBuildContext(string contextPath)
    {
      _buildContext = contextPath;
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
      _config.Commands.Add(new LabelCommand([.. nameValue.Select(x => (TemplateString)x)]));
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
      if (source.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase) ||
          source.StartsWith("ftps://", StringComparison.OrdinalIgnoreCase))
        throw new FluentDockerException("COPY URL sources only support HTTP/HTTPS; FTP/FTPS cannot be downloaded.");
      if (source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
          source.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
      {
        var uri = new Uri(source);
        var fileName = Path.GetFileName(uri.LocalPath);
        if (string.IsNullOrWhiteSpace(fileName))
          throw new FluentDockerException("COPY URL source must include a filename path segment.");
        var tmp = Path.Combine("___fluentdockerdl", fileName);
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
      _config.Commands.Add(new EnvCommand([.. nameValue.Select(x => (TemplateString)x)]));
      return this;
    }

    /// <summary>
    /// Adds a VOLUME instruction.
    /// </summary>
    public DockerfileBuilder Volume(params string[] mountpoints)
    {
      _config.Commands.Add(new VolumeCommand([.. mountpoints.Select(x => (TemplateString)x)]));
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
  }
}
