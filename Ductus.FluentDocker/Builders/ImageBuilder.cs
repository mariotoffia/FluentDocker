using System.Linq;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Extensions;
using Ductus.FluentDocker.Model.Builders;
using Ductus.FluentDocker.Model.Containers;
using Ductus.FluentDocker.Services;
using Ductus.FluentDocker.Services.Impl;

namespace Ductus.FluentDocker.Builders
{
  public sealed class ImageBuilder : BaseBuilder<IContainerImageService>
  {
    private readonly ImageBuilderConfig _config = new ImageBuilderConfig();
    private FileBuilder _fileBuilder;

    internal ImageBuilder(IBuilder parent) : base(parent)
    {
    }

    public override IContainerImageService Build()
    {
      if (string.IsNullOrEmpty(_config.ImageName))
      {
        throw new FluentDockerException("Cannot create or verify an image without a name");
      }

      var host = FindHostService();
      if (!host.HasValue)
      {
        throw new FluentDockerException(
          $"Cannot build Dockerfile for image {_config.ImageName} since no host service is defined");
      }

      var tag = null == _config.Params.Tags ? "latest" : _config.Params.Tags[0];
      var image = host.Value.GetImages().FirstOrDefault(x => x.Name == _config.ImageName && x.Tag == tag);

      if (_config.VerifyExistence && !_config.ForeRebuild)
      {
        return image;
      }

      if (null != image && !_config.ForeRebuild)
      {
        // Image already exists.
        return image;
      }

      if (null == _config.Params.Tags)
      {
        _config.Params.Tags = new[] {"latest"};
      }

      // Render docker file and copy all resources
      // to a working directory
      var workingdir = _fileBuilder.PrepareBuild();

      var id = host.Value.Build(_config.Params.Tags[0], workingdir, new ContainerBuildParams
      {
        Tags = _config.Params.Tags.Except(new[] {_config.Params.Tags[0]}).ToArray(),
        Quiet = true
      });

      if (id.IsFailure)
      {
        throw new FluentDockerException(
          $"Could not build image {_config.ImageName} due to error: {id.Error} log: {id.Log}");
      }

      return new DockerImageService(_config.ImageName, id.Value, _config.Params.Tags[0], host.Value.Host,
        host.Value.Certificates);
    }

    protected override IBuilder InternalCreate()
    {
      return new ImageBuilder(this);
    }

    public FileBuilder DefineFrom(string from)
    {
      return _fileBuilder = new FileBuilder(this).UseParent(from);
    }

    public ImageBuilder VerifyExistence()
    {
      _config.VerifyExistence = true;
      return this;
    }

    public ImageBuilder ForceRebuild()
    {
      _config.ForeRebuild = true;
      return this;
    }

    public ImageBuilder AsImageName(string name)
    {
      _config.ImageName = name;
      return this;
    }

    public ImageBuilder ImageTag(params string[] tags)
    {
      _config.Params.Tags.AddToArray(tags);
      return this;
    }

    public ImageBuilder BuildArguments(params string[] args)
    {
      _config.Params.BuildArguments.AddToArray(args);
      return this;
    }

    public ImageBuilder NoVerifyImage()
    {
      _config.Params.SkipImageVerification = true;
      return this;
    }

    public ImageBuilder Label(params string[] labels)
    {
      _config.Params.Labels.AddToArray(labels);
      return this;
    }

    public ImageBuilder NoCache()
    {
      _config.Params.NoCache = true;
      return this;
    }

    public ImageBuilder AlwaysPull()
    {
      _config.Params.AlwaysPull = true;
      return this;
    }

    public ImageBuilder WithIsolation(ContainerIsolationTechnology isolation)
    {
      _config.Params.Isolation = isolation;
      return this;
    }

    public ImageBuilder RemoveIntermediate(bool force = false)
    {
      _config.Params.RemoveIntermediateContainersOnSuccessfulBuild = true;
      if (force)
      {
        _config.Params.ForceRemoveIntermediateContainers = true;
      }
      return this;
    }
  }
}