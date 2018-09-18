using System;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Model.Compose;
using Ductus.FluentDocker.Model.Images;
using Ductus.FluentDocker.Services;
using Ductus.FluentDocker.Services.Impl;

namespace Ductus.FluentDocker.Builders
{
  public sealed class ComposeFileBuilder : BaseBuilder<ICompositeService>
  {
    private readonly DockerComposeConfig _config = new DockerComposeConfig();

    internal ComposeFileBuilder(IBuilder parent, string composeFile = null) : base(parent)
    {
      _config.ComposeFilePath = composeFile;
    }

    public override ICompositeService Build()
    {
      if (string.IsNullOrEmpty(_config.ComposeFilePath))
        throw new FluentDockerException("Cannot create service without a docker-compose file");

      var host = FindHostService();
      if (!host.HasValue)
        throw new FluentDockerException(
          $"Cannot build service using compose-file {_config.ComposeFilePath} since no host service is defined");

      return new DockerComposeCompositeService(host.Value, _config);
    }

    public ComposeFileBuilder FromFile(string composeFile)
    {
      _config.ComposeFilePath = composeFile;
      return this;
    }

    public ComposeFileBuilder ForceRecreate()
    {
      _config.ForceRecreate = true;
      return this;
    }

    public ComposeFileBuilder NoRecreate()
    {
      _config.NoRecreate = true;
      return this;
    }

    public ComposeFileBuilder NoBuild()
    {
      _config.NoBuild = true;
      return this;
    }

    public ComposeFileBuilder ForceBuild()
    {
      _config.ForceBuild = true;
      return this;
    }

    public ComposeFileBuilder Timeout(TimeSpan timeoutInSeconds)
    {
      _config.TimeoutSeconds = timeoutInSeconds;
      return this;
    }

    public ComposeFileBuilder RemoveOrphans()
    {
      _config.RemoveOrphans = true;
      return this;
    }

    public ComposeFileBuilder ServiceName(string name)
    {
      _config.AlternativeServiceName = name;
      return this;
    }

    public ComposeFileBuilder UseColor()
    {
      _config.UseColor = true;
      return this;
    }

    public ComposeFileBuilder KeepVolumes()
    {
      _config.KeepVolumes = true;
      return this;
    }

    public ComposeFileBuilder RemoveAllImages()
    {
      _config.ImageRemoval = ImageRemovalOption.All;
      return this;
    }

    public ComposeFileBuilder RemoveNonTaggedImages()
    {
      _config.ImageRemoval = ImageRemovalOption.Local;
      return this;
    }

    public ComposeFileBuilder KeepOnDispose()
    {
      _config.StopOnDispose = false;
      return this;
    }

    protected override IBuilder InternalCreate()
    {
      return new ComposeFileBuilder(this);
    }
  }
}