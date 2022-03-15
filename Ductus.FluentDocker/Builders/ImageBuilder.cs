using System.Linq;
using Ductus.FluentDocker.Common;
using Ductus.FluentDocker.Extensions;
using Ductus.FluentDocker.Model.Builders;
using Ductus.FluentDocker.Model.Common;
using Ductus.FluentDocker.Model.Containers;
using Ductus.FluentDocker.Services;
using Ductus.FluentDocker.Services.Extensions;
using Ductus.FluentDocker.Services.Impl;

namespace Ductus.FluentDocker.Builders
{
  public sealed class ImageBuilder : BaseBuilder<IContainerImageService>
  {
    internal readonly ImageBuilderConfig Config = new ImageBuilderConfig();
    private FileBuilder _fileBuilder;

    internal ImageBuilder(IBuilder parent) : base(parent)
    {
    }

    public override IContainerImageService Build()
    {
      if (string.IsNullOrEmpty(Config.ImageName))
        throw new FluentDockerException("Cannot create or verify an image without a name");

      var host = FindHostService();
      if (!host.HasValue)
        throw new FluentDockerException(
          $"Cannot build Dockerfile for image {Config.ImageName} since no host service is defined");

      var tag = null == Config.Params.Tags ? "latest" : Config.Params.Tags[0];
      var image = host.Value.GetImages().FirstOrDefault(x => x.Name == Config.ImageName && x.Tag == tag);

      if (Config.VerifyExistence && null != image)
        return image;

      if (null == Config.Params.Tags)
        Config.Params.Tags = new[] { "latest" };

      // Render docker file and copy all resources
      // to a working directory
      var workingdir = _fileBuilder.PrepareBuild();

      var id = host.Value.Build(Config.ImageName, Config.Params.Tags[0], workingdir, new ContainerBuildParams
        {
          BuildArguments = Config.Params.BuildArguments,
          Tags = Config.Params.Tags.Except(new[] {Config.Params.Tags[0]}).ToArray(),
          Quiet = true
        });

      if (id.IsFailure)
        throw new FluentDockerException(
          $"Could not build image {Config.ImageName} due to error: {id.Error} log: {id.Log}");

      return new DockerImageService(Config.ImageName, id.Value.ToPlainId(), Config.Params.Tags[0], host.Value.Host,
        host.Value.Certificates, Config.IsWindowsHost);
    }

    protected override IBuilder InternalCreate()
    {
      return new ImageBuilder(this);
    }

    /// <summary>
    /// Creates a _dockerfile_ builder.
    /// </summary>
    /// <param name="imageAndTag">
    /// Optional image to specify as FROM. If omitted, it is up to the caller to specify _UseParent_ or _From_.
    /// </param>
    /// <returns>
    /// A newly created file builder. If empty or null string the `FileBuilder` is empty. Otherwise it has populated
    /// the `FileBuilder` with a parent of the specified image name (via _UseParent()_).
    /// </returns>
    public FileBuilder From(string imageAndTag = null)
    {
      if (string.IsNullOrEmpty(imageAndTag))
      {
        return _fileBuilder = new FileBuilder(this);
      }

      return _fileBuilder = new FileBuilder(this).UseParent(imageAndTag);
    }

    public FileBuilder From(string imageAndTag, string asName)
    {
      if (string.IsNullOrEmpty(imageAndTag))
      {
        return _fileBuilder = new FileBuilder(this);
      }

      return _fileBuilder = new FileBuilder(this).From(imageAndTag, asName);
    }

    public FileBuilder FromFile(string dockerFile)
    {
      return _fileBuilder = new FileBuilder(this).FromFile(dockerFile);
    }

    public FileBuilder FromString(string dockerfileString)
    {
      return _fileBuilder = new FileBuilder(this).FromString(dockerfileString);
    }

    public ImageBuilder IsWindowsHost()
    {
      Config.IsWindowsHost = true;
      return this;
    }

    public ImageBuilder ReuseIfAlreadyExists()
    {
      Config.VerifyExistence = true;
      return this;
    }

    public ImageBuilder AsImageName(string name)
    {
      if (name == null) {
        return this;
      }

      var s = name.Split(':');
      if (s.Length == 2)
      {
        Config.ImageName = s[0];
        Config.Params.Tags = Config.Params.Tags.ArrayAdd(s[1]);
      }
      else
      {
        Config.ImageName = name;
        Config.Params.Tags = Config.Params.Tags.ArrayAdd("latest");
      }

      return this;
    }

    public ImageBuilder ImageTag(params string[] tags)
    {
      Config.Params.Tags = Config.Params.Tags.ArrayAddDistinct(tags);
      return this;
    }

    public ImageBuilder BuildArguments(params string[] args)
    {
      Config.Params.BuildArguments = Config.Params.BuildArguments.ArrayAdd(args);
      return this;
    }

    /// <summary>
    /// Sets the file name of the rendered Dockerfile to the specified <paramref name="dockerfileName"/>.
    /// Default of <see cref="ContainerBuildParams.File"/> is 'PATH/Dockerfile'.
    /// </summary>
    /// <param name="dockerfileName"></param>
    /// <returns></returns>
    /// <remarks><inheritdoc cref="ContainerBuildParams.File"/></remarks>
    public ImageBuilder DockerfileName(TemplateString dockerfileName)
    {
      Config.Params.File = dockerfileName;
      return this;
    }

    public ImageBuilder NoVerifyImage()
    {
      Config.Params.SkipImageVerification = true;
      return this;
    }

    public ImageBuilder Label(params string[] labels)
    {
      Config.Params.Labels = Config.Params.Labels.ArrayAdd(labels);
      return this;
    }

    public ImageBuilder NoCache()
    {
      Config.Params.NoCache = true;
      return this;
    }

    public ImageBuilder AlwaysPull()
    {
      Config.Params.AlwaysPull = true;
      return this;
    }

    public ImageBuilder WithIsolation(ContainerIsolationTechnology isolation)
    {
      Config.Params.Isolation = isolation;
      return this;
    }

    public ImageBuilder RemoveIntermediate(bool force = false)
    {
      Config.Params.RemoveIntermediateContainersOnSuccessfulBuild = true;
      if (force)
        Config.Params.ForceRemoveIntermediateContainers = true;
      return this;
    }
  }
}
