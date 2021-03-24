using System;
using System.IO;
using System.Linq;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Extensions;
using Ductus.FluentDocker.Model.Builders;
using Ductus.FluentDocker.Model.Builders.FileBuilder;
using Ductus.FluentDocker.Model.Common;
using Ductus.FluentDocker.Services;

namespace Ductus.FluentDocker.Builders
{
  public sealed class FileBuilder
  {
    private readonly FileBuilderConfig _config = new FileBuilderConfig();
    private readonly ImageBuilder _parent;
    private TemplateString _workingFolder = @"${TEMP}/fluentdockertest/${RND}";
    private string _lastContents;

    internal FileBuilder(ImageBuilder parent) => _parent = parent;

    /// <summary>
    /// Used to create a Dockerfile as string
    /// </summary>
    /// <remarks>
    ///   This is mainly for unit testing but may be used by external
    ///   code to render Dockerfile for custom usage. Use the
    ///   <see cref="ToDockerfileString()"/> method to produce the
    ///   Dockerfile contents (and copy files / render Dockerfile
    ///   on working directory). If no working directory is set
    ///   it will create a temporary one.
    /// </remarks>
    public FileBuilder()
    {
    }

    internal string PrepareBuild()
    {

      CopyToWorkDir(_workingFolder); // Must be before RenderDockerFile!
      RenderDockerfile(_workingFolder);
      return _workingFolder;
    }

    public IContainerImageService Build()
    {
      if (null == _parent)
      {
        throw new FluentDockerException("No ImageBuilder was set as parent");
      }

      return _parent.Build();
    }

    public Builder Builder()
    {
      return _parent.Builder();
    }

    public ImageBuilder ToImage()
    {
      if (null == _parent)
      {
        throw new FluentDockerException("No ImageBuilder was set as parent");
      }

      return _parent;
    }

    public FileBuilder WorkingFolder(TemplateString workingFolder)
    {
      _workingFolder = workingFolder;
      return this;
    }

    /// <summary>
    /// Specified a simplified from command with the image name (and optional tag name) only!
    /// </summary>
    /// <param name="from">The image and optional tag name.</param>
    /// <returns>Itself for fluent access.</returns>
    public FileBuilder UseParent(string from)
    {
      _config.Commands.Add(new FromCommand(from));
      return this;
    }

    /// <summary>
    /// Specifies the _FROM_ command.
    /// </summary>
    /// <param name="imageAndTag">The image to derive from and a optional (colon) tag, e.g. myimg:mytag</param>
    /// <param name="asName">An optional alias.</param>
    /// <param name="platform">An optional platform such linux/amd64 or windows/amd64.</param>
    /// <returns>Itself for fluent access.</returns>
    public FileBuilder From(TemplateString imageAndTag, TemplateString asName = null, TemplateString platform = null)
    {
      _config.Commands.Add(new FromCommand(imageAndTag, asName, platform));
      return this;
    }

    public FileBuilder Maintainer(string maintainer)
    {
      _config.Commands.Add(new MaintainerCommand(maintainer));
      return this;
    }

    public FileBuilder Add(TemplateString source, TemplateString destination)
    {
      _config.Commands.Add(new AddCommand(source, destination));
      return this;
    }

    public FileBuilder Shell(string command, params string[] args)
    {
      _config.Commands.Add(new ShellCommand(command, args));
      return this;
    }

    public FileBuilder Run(params TemplateString[] commands)
    {
      foreach (var cmd in commands)
      {
        _config.Commands.Add(new RunCommand(cmd));
      }
      return this;
    }

    /// <summary>
    /// Adds an HEALTHCHECK clause of which gets executed when container is started/running.
    /// </summary>
    /// <param name="cmd">The command with it's argument to do when performing the health check.</param>
    /// <param name="interval">Optional (default is 30s) interval when to invoke the <paramref name="cmd"/>.</param>
    /// <param name="timeout">Optional (default is 30s) when the healthcheck is force cancelled and failed.</param>
    /// <param name="startPeriod">Optional (default is 0s) when it shall start to execute the <paramref name="cmd"/>.</param>
    /// <param name="retries">Optional (default is 3) number retries before consider it as non healthy.</param>
    /// <remarks>
    ///   A <paramref name="cmd"/> can be e.g. a curl command combined by other shell command for example:
    ///   "curl -f http://localhost/ || exit 1".
    /// </remarks>
    public FileBuilder WithHealthCheck(string cmd, string interval = null, string timeout = null, string startPeriod = null, int retries = 3)
    {
      _config.Commands.Add(new HealthCheckCommand(cmd, interval, timeout, startPeriod, retries));
      return this;
    }

    /// <summary>
    /// This generates the _COPY_ command.
    /// </summary>
    /// <param name="source">From directory.</param>
    /// <param name="dest">To directory.</param>
    /// <param name="chownUserAndGroup">Optional --chown user:group.</param>
    /// <param name="fromAlias">
    /// Optional source location from earlier buildstage FROM ... AS alias. This will 
    /// generate --from=aliasname in the _COPY_ command and hence reference a earlier
    /// _FROM ... AS aliasname_ buildstep as source.
    /// </param>
    /// <returns>Itself for fluent access.</returns>
    /// <remarks>
    /// Initial support for downloading files from a _URL_, just specify a full http(s)
    /// to a resource. It will place those resources under _{workingfolder}/___fluentdockerdl_
    /// without any sub-directories. Hence, it is not possible to have multiple _URLs_ with
    /// same filename - the last one downloaded will be the one used.
    /// </remarks>
    public FileBuilder Copy(TemplateString source, TemplateString dest,
    TemplateString chownUserAndGroup = null, TemplateString fromAlias = null)
    {
      var lc = source.Rendered.ToLower();
      if (lc.StartsWith("http://") || lc.StartsWith("https://") ||
        lc.StartsWith("ftp://") || lc.StartsWith("ftps://"))
      {

        var uri = new Uri(source);
        var tmp = Path.Combine("___fluentdockerdl", Path.GetFileName(uri.LocalPath));

        _config.Commands.Add(
            new CopyURLCommand(
              uri, tmp, dest, chownUserAndGroup, fromAlias
            )
          );

        return this;
      }

      _config.Commands.Add(new CopyCommand(source, dest, chownUserAndGroup, fromAlias));
      return this;
    }

    public FileBuilder UseWorkDir(string workdir)
    {
      _config.Commands.Add(new WorkdirCommand(workdir));
      return this;
    }

    public FileBuilder ExposePorts(params int[] ports)
    {
      _config.Commands.Add(new ExposeCommand(ports));
      return this;
    }

    /// <summary>
    /// Adds a _ENV_ command to _dockerfile_. The value of each name value pair is automatically
    /// double quoted. Hence, it is possible to write spaces etc in the string without double quoting it.
    /// </summary>
    /// <param name="nameValue">Name=value array.</param>
    /// <returns>Itself for fluent access.</returns>
    /// <remarks>The name value is separated by space on same line.</remarks>
    public FileBuilder Environment(params TemplateString[] nameValue)
    {
      _config.Commands.Add(new EnvCommand(nameValue));
      return this;
    }

    /// <summary>
    /// Adds a _LABEL_ command to _dockerfile_. The value of each name value pair is automatically
    /// double quoted. Hence, it is possible to write spaces etc in the string without double quoting it.
    /// </summary>
    /// <param name="nameValue">Name=value array.</param>
    /// <returns>Itself for fluent access.</returns>
    /// <remarks>The name value is separated by space on same line.</remarks>
    public FileBuilder Label(params TemplateString[] nameValue)
    {
      _config.Commands.Add(new LabelCommand(nameValue));
      return this;
    }

    /// <summary>
    /// Adds a _ARG_ command in _dockerfile_ with the optional _defaultValue_.
    /// </summary>
    /// <param name="name">The name of the argument.</param>
    /// <param name="defaultValue">Optional a default value for the argument.</param>
    /// <returns>Itself for fluent access.</returns>
    public FileBuilder Arguments(TemplateString name, TemplateString defaultValue = null)
    {
      _config.Commands.Add(new ArgCommand(name, defaultValue));
      return this;
    }

    public FileBuilder Entrypoint(string command, params string[] args)
    {
      _config.Commands.Add(new EntrypointCommand(command, args));
      return this;
    }

    public FileBuilder User(TemplateString user, TemplateString group = null)
    {
      _config.Commands.Add(new UserCommand(user, group));
      return this;
    }

    public FileBuilder Volume(params TemplateString[] mountpoints)
    {
      _config.Commands.Add(new VolumeCommand(mountpoints));
      return this;
    }

    public FileBuilder Command(string command, params string[] args)
    {
      _config.Commands.Add(new CmdCommand(command, args));
      return this;
    }

    public FileBuilder FromFile(string file)
    {
      _config.UseFile = file;
      return this;
    }

    public FileBuilder FromString(string dockerFileAsString)
    {
      _config.DockerFileString = dockerFileAsString;
      return this;
    }

    /// <summary>
    ///   Copies all files and folders to working directory.
    ///   Note that this method will mutate <see cref="AddCommand.Source" /> to
    ///   a relative path!!
    /// </summary>
    /// <param name="workingFolder">The working folder.</param>
    private void CopyToWorkDir(TemplateString workingFolder)
    {
      // Copy all files from copy arguments.
      foreach (var cp in _config.Commands.Where(x => x is CopyCommand).Cast<CopyCommand>())
      {
        if (cp is CopyURLCommand)
        {
          var wdlp = Path.Combine(workingFolder, cp.From);
          var dd = Path.GetDirectoryName(wdlp);

          if (dd != "")
          {
            Directory.CreateDirectory(dd);
          }

          var ccp = (CopyURLCommand)cp;
          var res = ccp.FromURL.Download(wdlp).Result;

          continue;
        }

        // Standard CopyCommand

        if (!File.Exists(cp.From))
          continue;

        var wp = Path.Combine(workingFolder, cp.From);
        var wdp = Path.GetDirectoryName(wp);
        Directory.CreateDirectory(wdp);

        File.Copy(cp.From, wp, true /*overwrite*/);

      }

      foreach (var command in _config.Commands.Where(x => x is AddCommand).Cast<AddCommand>())
      {
        var wff = Path.Combine(workingFolder, command.Source);
        if (File.Exists(wff) || Directory.Exists(wff))
        {
          // File or folder already at working folder - no need to copy.
          continue;
        }

        // Replace to relative path
        command.Source = command.Source.Copy(workingFolder);
      }
    }

    private void RenderDockerfile(TemplateString workingFolder)
    {
      if (Directory.Exists(workingFolder))
      {
        Directory.CreateDirectory(workingFolder);
      }

      var dockerFile = Path.Combine(workingFolder, "Dockerfile");

      var contents = !string.IsNullOrEmpty(_config.UseFile) ?
        _config.UseFile.FromFile() :
        ResolveOrBuildString();

      contents.ToFile(dockerFile);

      _lastContents = contents;
    }

    private string ResolveOrBuildString()
    {
      return !string.IsNullOrWhiteSpace(_config.DockerFileString) ?
              _config.DockerFileString :
              _config.ToString();
    }

    public string ToDockerfileString()
    {
      PrepareBuild();
      return _lastContents;
    }
  }
}
