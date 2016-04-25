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

      if (_config.VerifyExistence && null != image)
      {
        return image;
      }

      if (null == _config.Params.Tags)
      {
        _config.Params.Tags = new[] {"latest"};
      }

      // Render docker file and copy all resources
      // to a working directory
      var workingdir = _fileBuilder.PrepareBuild();

      var id = host.Value.Build(_config.ImageName, _config.Params.Tags[0], workingdir, new ContainerBuildParams
      {
        Tags = _config.Params.Tags.Except(new[] {_config.Params.Tags[0]}).ToArray(),
        Quiet = true
      });

      if (id.IsFailure)
      {
        throw new FluentDockerException(
          $"Could not build image {_config.ImageName} due to error: {id.Error} log: {id.Log}");
      }

      return new DockerImageService(_config.ImageName, id.Value.Strip(), _config.Params.Tags[0], host.Value.Host,
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

    public ImageBuilder ReuseIfAlreadyExists()
    {
      _config.VerifyExistence = true;
      return this;
    }

    public ImageBuilder AsImageName(string name)
    {
      var s = name.Split(':');
      if (s.Length == 2)
      {
        _config.ImageName = s[0];
        _config.Params.Tags.AddToArray(s[1]);
      }
      else
      {
        _config.ImageName = name;
        _config.Params.Tags.AddToArray("latest");
      }

      return this;
    }

    public ImageBuilder ImageTag(params string[] tags)
    {
      _config.Params.Tags.AddToArrayDistinct(tags);
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