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
    private TemplateString _workingFolder;

    internal FileBuilder(ImageBuilder parent)
    {
      _parent = parent;
    }

    /// <summary>
    /// Used to create a Dockerfile as string
    /// </summary>
    /// <remarks>
    ///   This is mainly for unit testing but may be used by external
    ///   code to render Dockerfile for custom usage. Use the
    ///   <see cref="ToDockerfileString()"/> method to produce the
    ///   Dockerfile contents.
    /// </remarks>
    public FileBuilder()
    {
    }

    internal string PrepareBuild()
    {
      if (null == _workingFolder)
      {
        _workingFolder = @"${TEMP}/fluentdockertest/${RND}";
      }

      CopyToWorkDir(_workingFolder); // Must be before RenderDockerFile!
      RenderDockerfile(_workingFolder);
      return _workingFolder;
    }

    public IContainerImageService Build()
    {
      if (null == _parent)
      {
        PrepareBuild();
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

    public FileBuilder UseParent(string from)
    {
      _config.Commands.Add(new FromCommand(from));
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
      foreach(var cmd in commands)
      {
        _config.Commands.Add(new RunCommand(cmd));
      }
      return this;
    }

    public FileBuilder Copy(TemplateString source, TemplateString dest)
    {
      _config.Commands.Add(new CopyCommand(source, dest));
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
        if (!File.Exists(cp.From)) continue;
        
        var wp = Path.Combine(workingFolder, cp.From);
        var wdp = Path.GetDirectoryName(wp);
        Directory.CreateDirectory(wdp);
        File.Copy(cp.From,wp, true /*overwrite*/);
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
      
      string contents = !string.IsNullOrEmpty(_config.UseFile) ? 
        _config.UseFile.FromFile() : 
        ToDockerfileString();

      contents.ToFile(dockerFile);
    }

    public string ToDockerfileString()
    {
      return !string.IsNullOrWhiteSpace(_config.DockerFileString) ? 
        _config.DockerFileString : 
        _config.ToString();
    }
  }
}