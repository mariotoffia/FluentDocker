using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Builders;
using FluentDocker.Model.Drivers;
using FluentDocker.Services;
using FluentDocker.Services.Impl;

namespace FluentDocker.Builders
{
  /// <summary>
  /// Builder interface for creating Docker images.
  /// </summary>
  public interface IImageBuilder
  {
    #region Dockerfile Configuration

    /// <summary>
    /// Creates a Dockerfile builder from a base image.
    /// </summary>
    DockerfileBuilder From(string imageAndTag);

    /// <summary>
    /// Creates a Dockerfile builder from a base image with alias.
    /// </summary>
    DockerfileBuilder From(string imageAndTag, string asName);

    /// <summary>
    /// Uses an existing Dockerfile from a file.
    /// </summary>
    DockerfileBuilder FromFile(string dockerFile);

    /// <summary>
    /// Uses a Dockerfile content string.
    /// </summary>
    DockerfileBuilder FromString(string dockerfileString);

    #endregion

    #region Image Configuration

    /// <summary>
    /// Reuse existing image if it already exists with the same name/tag.
    /// </summary>
    IImageBuilder ReuseIfAlreadyExists();

    /// <summary>
    /// Sets the image name.
    /// </summary>
    IImageBuilder AsImageName(string name);

    /// <summary>
    /// Adds tags to the image.
    /// </summary>
    IImageBuilder ImageTag(params string[] tags);

    /// <summary>
    /// Adds build arguments.
    /// </summary>
    IImageBuilder BuildArguments(params string[] args);

    /// <summary>
    /// Adds labels to the image.
    /// </summary>
    IImageBuilder Label(params string[] labels);

    /// <summary>
    /// Disables build cache.
    /// </summary>
    IImageBuilder NoCache();

    /// <summary>
    /// Always pull base images.
    /// </summary>
    IImageBuilder AlwaysPull();

    /// <summary>
    /// Removes intermediate containers after successful build.
    /// </summary>
    IImageBuilder RemoveIntermediate(bool force = false);

    /// <summary>
    /// Sets the target platform.
    /// </summary>
    IImageBuilder Platform(string platform);

    /// <summary>
    /// Sets the target build stage for multi-stage builds.
    /// </summary>
    IImageBuilder Target(string target);

    #endregion
  }

  /// <summary>
  /// Fluent builder for creating Docker images.
  /// </summary>
  public sealed class ImageBuilder : IImageBuilder, IDriverScopedBuilder
  {
    private readonly FluentDockerKernel _kernel;
    private readonly string _driverId;

    /// <inheritdoc />
    FluentDockerKernel IDriverScopedBuilder.Kernel => _kernel;

    /// <inheritdoc />
    string IDriverScopedBuilder.DriverId => _driverId;
    private static readonly char[] EqualsSeparator = ['='];
    private readonly ImageBuilderConfig _config = new();
    private DockerfileBuilder _dockerfileBuilder;

    private string _imageName;
    private readonly List<string> _tags = [];
    private readonly Dictionary<string, string> _buildArgs = [];
    private readonly Dictionary<string, string> _labels = [];
    private bool _reuseIfExists;
    private bool _noCache;
    private bool _alwaysPull;
    private bool _removeIntermediate;
    private bool _forceRemoveIntermediate;
    private string _platform;
    private string _target;

    /// <summary>
    /// Creates an ImageBuilder with kernel context.
    /// </summary>
    public ImageBuilder(FluentDockerKernel kernel, string driverId)
    {
      ArgumentNullException.ThrowIfNull(kernel);
      ArgumentNullException.ThrowIfNull(driverId);
      _kernel = kernel;
      _driverId = driverId;
    }

    /// <summary>
    /// Creates an ImageBuilder with the specified image name.
    /// </summary>
    public ImageBuilder(FluentDockerKernel kernel, string driverId, string imageName) : this(kernel, driverId) => SetImageName(imageName);

    #region IImageBuilder Implementation

    public DockerfileBuilder From(string imageAndTag = null)
    {
      _dockerfileBuilder = string.IsNullOrEmpty(imageAndTag)
          ? new DockerfileBuilder(this)
          : new DockerfileBuilder(this).UseParent(imageAndTag);
      return _dockerfileBuilder;
    }

    public DockerfileBuilder From(string imageAndTag, string asName)
    {
      _dockerfileBuilder = string.IsNullOrEmpty(imageAndTag)
          ? new DockerfileBuilder(this)
          : new DockerfileBuilder(this).From(imageAndTag, asName);
      return _dockerfileBuilder;
    }

    public DockerfileBuilder FromFile(string dockerFile)
    {
      _dockerfileBuilder = new DockerfileBuilder(this).FromFile(dockerFile);
      return _dockerfileBuilder;
    }

    public DockerfileBuilder FromString(string dockerfileString)
    {
      _dockerfileBuilder = new DockerfileBuilder(this).FromString(dockerfileString);
      return _dockerfileBuilder;
    }

    public IImageBuilder ReuseIfAlreadyExists()
    {
      _reuseIfExists = true;
      return this;
    }

    public IImageBuilder AsImageName(string name)
    {
      SetImageName(name);
      return this;
    }

    public IImageBuilder ImageTag(params string[] tags)
    {
      foreach (var tag in tags)
      {
        if (!_tags.Contains(tag))
          _tags.Add(tag);
      }
      return this;
    }

    public IImageBuilder BuildArguments(params string[] args)
    {
      foreach (var arg in args)
      {
        var parts = arg.Split(EqualsSeparator, 2);
        if (parts.Length == 2)
          _buildArgs[parts[0]] = parts[1];
        else
          _buildArgs[parts[0]] = "";
      }
      return this;
    }

    public IImageBuilder Label(params string[] labels)
    {
      foreach (var label in labels)
      {
        var parts = label.Split(EqualsSeparator, 2);
        if (parts.Length == 2)
          _labels[parts[0]] = parts[1];
        else
          _labels[parts[0]] = "";
      }
      return this;
    }

    public IImageBuilder NoCache()
    {
      _noCache = true;
      return this;
    }

    public IImageBuilder AlwaysPull()
    {
      _alwaysPull = true;
      return this;
    }

    public IImageBuilder RemoveIntermediate(bool force = false)
    {
      _removeIntermediate = true;
      _forceRemoveIntermediate = force;
      return this;
    }

    public IImageBuilder Platform(string platform)
    {
      _platform = platform;
      return this;
    }

    public IImageBuilder Target(string target)
    {
      _target = target;
      return this;
    }

    #endregion

    #region Build Execution

    /// <summary>
    /// Executes the image build operation.
    /// </summary>
    internal async Task<IImageService> ExecuteAsync(CancellationToken cancellationToken)
    {
      if (string.IsNullOrEmpty(_imageName))
        throw new FluentDockerException("Cannot build an image without a name. Use AsImageName() or provide name in DefineImage().");

      if (_dockerfileBuilder == null)
        throw new FluentDockerException("No Dockerfile defined. Use From(), FromFile(), or FromString() to define one.");

      var driver = _kernel.SysCtl<IImageDriver>(_driverId);
      var context = new DriverContext(_driverId);

      // If reuse is enabled, check if image already exists
      if (_reuseIfExists)
      {
        var tag = _tags.Count > 0 ? _tags[0] : "latest";
        var existingImages = await driver.ListAsync(context, new ImageListFilter
        {
          Reference = $"{_imageName}:{tag}"
        }, cancellationToken).ConfigureAwait(false);

        if (existingImages.Success && existingImages.Data.Count > 0)
        {
          var existing = existingImages.Data[0];
          return new ImageService(_kernel, _driverId, existing.Id, _imageName, tag);
        }
      }

      // Prepare build context (copy files, render Dockerfile)
      var buildContext = await _dockerfileBuilder.PrepareBuildAsync().ConfigureAwait(false);

      // Ensure at least one tag
      if (_tags.Count == 0)
        _tags.Add("latest");

      // Build the image
      var buildConfig = new ImageBuildConfig
      {
        BuildContext = buildContext,
        DockerfileName = _dockerfileBuilder.PreparedDockerfileName,
        Tags = [.. _tags.Select(t => $"{_imageName}:{t}")],
        BuildArgs = _buildArgs,
        Labels = _labels,
        NoCache = _noCache,
        Pull = _alwaysPull,
        Rm = _removeIntermediate,
        ForceRm = _forceRemoveIntermediate,
        Platform = _platform,
        Target = _target
      };

      var result = await driver.BuildAsync(context, buildConfig, null, cancellationToken).ConfigureAwait(false);

      if (!result.Success)
        throw new FluentDockerException($"Failed to build image {_imageName}: {result.Error}");

      return new ImageService(_kernel, _driverId, result.Data.ImageId, _imageName, _tags[0]);
    }

    private void SetImageName(string name)
    {
      if (string.IsNullOrEmpty(name))
        return;

      var parts = name.Split(':');
      if (parts.Length == 2)
      {
        _imageName = parts[0];
        if (!_tags.Contains(parts[1]))
          _tags.Add(parts[1]);
      }
      else
      {
        _imageName = name;
      }
    }

    #endregion
  }
}

