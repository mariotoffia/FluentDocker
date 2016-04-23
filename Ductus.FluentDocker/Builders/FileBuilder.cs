using System.Collections.Generic;
using System.IO;
using Ductus.FluentDocker.Extensions;
using Ductus.FluentDocker.Model.Builders;
using Ductus.FluentDocker.Model.Common;
using Ductus.FluentDocker.Services;

namespace Ductus.FluentDocker.Builders
{
  public sealed class FileBuilder : BaseBuilder<IContainerImageService>
  {
    private readonly FileBuilderConfig _config = new FileBuilderConfig();

    internal FileBuilder(IBuilder parent) : base(parent)
    {
    }

    public override IContainerImageService Build()
    {
      TemplateString workingFolder = "${TMP}/";
      if (!string.IsNullOrWhiteSpace(_config.UseFile))
      {
        //TODO: _config.UseFile.Rendered.WriteFile();
      }
      throw new System.NotImplementedException();
    }

    protected override IBuilder InternalCreate()
    {
      return new FileBuilder(this);
    }

    public FileBuilder UseParent(string from)
    {
      _config.From = from;
      return this;
    }

    public FileBuilder Maintainer(string maintainer)
    {
      _config.Maintainer = maintainer;
      return this;
    }

    public FileBuilder Build(CommandType type, params string[] commands)
    {
      _config.BuildCommands.Add(new BuildCommand {Command = type, Lines = new List<string>(commands)});
      return this;
    }

    public FileBuilder UseWorkDir(string workdir)
    {
      _config.Workdir = workdir;
      return this;
    }

    public FileBuilder ExposePorts(int[] ports)
    {
      _config.Expose = new List<int>(ports);
      return this;
    }

    public FileBuilder Execute(string command, string[] args)
    {
      _config.Command.Add(command);
      if (null != args && 0 != args.Length)
      {
        ((List<string>) _config.Command).AddRange(args);
      }
      return this;
    }

    public FileBuilder VerifyExistence()
    {
      _config.VerifyExistenceOnly = true;
      return this;
    }

    public FileBuilder UseImageTags(params string[] tags)
    {
      ((List<string>)_config.Tags).AddRange(tags);
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
  }
}