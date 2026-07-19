using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
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
    /// <param name="imageAndTag">Base image reference, or null for an empty Dockerfile builder.</param>
    /// <returns>The Dockerfile builder for defining image contents.</returns>
    DockerfileBuilder From(string imageAndTag);

    /// <summary>
    /// Creates a Dockerfile builder from a base image with alias.
    /// </summary>
    /// <param name="imageAndTag">Base image reference.</param>
    /// <param name="asName">Stage alias.</param>
    /// <returns>The Dockerfile builder for defining image contents.</returns>
    /// <exception cref="ArgumentException"><paramref name="imageAndTag"/> is null or empty (the stage alias would otherwise be silently discarded).</exception>
    DockerfileBuilder From(string imageAndTag, string asName);

    /// <summary>
    /// Uses an existing Dockerfile from a file.
    /// </summary>
    /// <param name="dockerFile">Path to the Dockerfile.</param>
    /// <returns>The Dockerfile builder for build-context configuration.</returns>
    DockerfileBuilder FromFile(string dockerFile);

    /// <summary>
    /// Uses a Dockerfile content string.
    /// </summary>
    /// <param name="dockerfileString">Dockerfile contents.</param>
    /// <returns>The Dockerfile builder for build-context configuration.</returns>
    DockerfileBuilder FromString(string dockerfileString);

    #endregion

    #region Image Configuration

    /// <summary>
    /// Reuse existing image if it already exists with the same name/tag.
    /// </summary>
    /// <returns>The builder instance for method chaining.</returns>
    IImageBuilder ReuseIfAlreadyExists();

    /// <summary>
    /// Sets the image name.
    /// </summary>
    /// <param name="name">Image repository name with optional tag.</param>
    /// <returns>The builder instance for method chaining.</returns>
    IImageBuilder AsImageName(string name);

    /// <summary>
    /// Adds tags to the image.
    /// </summary>
    /// <param name="tags">Tag names without the image repository prefix.</param>
    /// <returns>The builder instance for method chaining.</returns>
    IImageBuilder ImageTag(params string[] tags);

    /// <summary>
    /// Adds build arguments in <c>KEY=VALUE</c> format; entries without <c>=</c> use an empty value.
    /// </summary>
    /// <param name="args">Build arguments in <c>KEY=VALUE</c> format.</param>
    /// <returns>The builder instance for method chaining.</returns>
    IImageBuilder BuildArguments(params string[] args);

    /// <summary>
    /// Adds labels in <c>KEY=VALUE</c> format; entries without <c>=</c> use an empty value.
    /// </summary>
    /// <remarks>
    /// This passes build labels directly to the image builder. For Dockerfile <c>LABEL</c>
    /// instructions, see <see cref="DockerfileBuilder.Label"/> for Docker <c>$</c> expansion rules.
    /// </remarks>
    /// <param name="labels">Labels in <c>KEY=VALUE</c> format.</param>
    /// <returns>The builder instance for method chaining.</returns>
    IImageBuilder Label(params string[] labels);

    /// <summary>
    /// Disables build cache.
    /// </summary>
    /// <returns>The builder instance for method chaining.</returns>
    IImageBuilder NoCache();

    /// <summary>
    /// Always pull base images.
    /// </summary>
    /// <returns>The builder instance for method chaining.</returns>
    IImageBuilder AlwaysPull();

    /// <summary>
    /// Removes intermediate containers after successful build.
    /// </summary>
    /// <param name="force">Force removal of intermediate containers.</param>
    /// <returns>The builder instance for method chaining.</returns>
    IImageBuilder RemoveIntermediate(bool force = false);

    /// <summary>
    /// Sets the target platform.
    /// </summary>
    /// <param name="platform">Target platform, for example <c>linux/amd64</c>.</param>
    /// <returns>The builder instance for method chaining.</returns>
    IImageBuilder Platform(string platform);

    /// <summary>
    /// Sets the target build stage for multi-stage builds.
    /// </summary>
    /// <param name="target">Target stage name.</param>
    /// <returns>The builder instance for method chaining.</returns>
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
    public ImageBuilder(FluentDockerKernel kernel, string driverId, string imageName) : this(kernel, driverId)
    {
      if (!string.IsNullOrEmpty(imageName))
        SetImageName(imageName);
    }

    #region IImageBuilder Implementation

    /// <inheritdoc />
    public DockerfileBuilder From(string? imageAndTag = null)
    {
      _dockerfileBuilder = string.IsNullOrEmpty(imageAndTag)
          ? new DockerfileBuilder(this)
          : new DockerfileBuilder(this).UseParent(imageAndTag);
      return _dockerfileBuilder;
    }

    /// <inheritdoc />
    public DockerfileBuilder From(string imageAndTag, string asName)
    {
      // Unlike From(string), an empty image here would silently discard the stage alias.
      ArgumentException.ThrowIfNullOrEmpty(imageAndTag);
      _dockerfileBuilder = new DockerfileBuilder(this).From(imageAndTag, asName);
      return _dockerfileBuilder;
    }

    /// <inheritdoc />
    public DockerfileBuilder FromFile(string dockerFile)
    {
      _dockerfileBuilder = new DockerfileBuilder(this).FromFile(dockerFile);
      return _dockerfileBuilder;
    }

    /// <inheritdoc />
    public DockerfileBuilder FromString(string dockerfileString)
    {
      _dockerfileBuilder = new DockerfileBuilder(this).FromString(dockerfileString);
      return _dockerfileBuilder;
    }

    /// <inheritdoc />
    public IImageBuilder ReuseIfAlreadyExists()
    {
      _reuseIfExists = true;
      return this;
    }

    /// <inheritdoc />
    public IImageBuilder AsImageName(string name)
    {
      SetImageName(name);
      return this;
    }

    /// <inheritdoc />
    public IImageBuilder ImageTag(params string[] tags)
    {
      ArgumentNullException.ThrowIfNull(tags);
      foreach (var tag in tags)
      {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        if (!_tags.Contains(tag))
          _tags.Add(tag);
      }
      return this;
    }

    /// <inheritdoc />
    public IImageBuilder BuildArguments(params string[] args)
    {
      ArgumentNullException.ThrowIfNull(args);
      foreach (var arg in args)
      {
        ArgumentNullException.ThrowIfNull(arg);
        var parts = arg.Split(EqualsSeparator, 2);
        if (parts.Length == 2)
          _buildArgs[parts[0]] = parts[1];
        else
          _buildArgs[parts[0]] = "";
      }
      return this;
    }

    /// <inheritdoc />
    public IImageBuilder Label(params string[] labels)
    {
      ArgumentNullException.ThrowIfNull(labels);
      foreach (var label in labels)
      {
        ArgumentNullException.ThrowIfNull(label);
        var parts = label.Split(EqualsSeparator, 2);
        if (parts.Length == 2)
          _labels[parts[0]] = parts[1];
        else
          _labels[parts[0]] = "";
      }
      return this;
    }

    /// <inheritdoc />
    public IImageBuilder NoCache()
    {
      _noCache = true;
      return this;
    }

    /// <inheritdoc />
    public IImageBuilder AlwaysPull()
    {
      _alwaysPull = true;
      return this;
    }

    /// <inheritdoc />
    public IImageBuilder RemoveIntermediate(bool force = false)
    {
      _removeIntermediate = true;
      _forceRemoveIntermediate = force;
      return this;
    }

    /// <inheritdoc />
    public IImageBuilder Platform(string platform)
    {
      _platform = platform;
      return this;
    }

    /// <inheritdoc />
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
        throw new FluentDockerException("Cannot build an image without a name. Use AsImageName() or pass a name to UseImage().");

      if (_dockerfileBuilder == null)
        throw new FluentDockerException("No Dockerfile defined. Use From(), FromFile(), or FromString() to define one.");

      var driver = _kernel.SysCtl<IImageDriver>(_driverId);
      var context = new DriverContext(_driverId);
      EnsureDefaultTag();

      if (_reuseIfExists)
      {
        var existing = await TryResolveReusableImageAsync(driver, context, cancellationToken).ConfigureAwait(false);
        if (existing != null)
          return existing;
      }

      try
      {
        // Prepare build context (copy files, render Dockerfile)
        var buildContext = await _dockerfileBuilder.PrepareBuildAsync(
            strictCopySources: true, cancellationToken).ConfigureAwait(false);
        if (!_dockerfileBuilder.HasFromInstruction)
          throw new FluentDockerException("Cannot build a Dockerfile with no FROM instruction.");

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
      finally
      {
        _dockerfileBuilder.DeleteOwnedWorkingFolder();
      }
    }

    private void SetImageName(string name)
    {
      if (string.IsNullOrEmpty(name))
        throw new ArgumentException("Image name cannot be null or empty.", nameof(name));
      if (name.Contains('@', StringComparison.Ordinal))
        throw new FluentDockerException(
            $"Digest image references are not valid build output names: '{name}'. Use a repository[:tag] name.");

      var (image, tag) = ContainerBuilder.ParseImageReference(name);
      _imageName = image;
      if (!string.IsNullOrEmpty(tag) && tag != "latest")
      {
        if (!_tags.Contains(tag))
          _tags.Add(tag);
      }
    }

    private void EnsureDefaultTag()
    {
      if (_tags.Count == 0)
        _tags.Add("latest");
    }

    private async Task<IImageService> TryResolveReusableImageAsync(
        IImageDriver driver, DriverContext context, CancellationToken cancellationToken)
    {
      string imageId = null;
      foreach (var tag in _tags)
      {
        var existingImages = await driver.ListAsync(context, new ImageListFilter
        {
          Reference = $"{_imageName}:{tag}"
        }, cancellationToken).ConfigureAwait(false);
        var existing = existingImages.Success ? existingImages.Data?.FirstOrDefault() : null;
        if (existing?.Id == null)
          return null;
        if (imageId == null)
          imageId = existing.Id;
        else if (!string.Equals(imageId, existing.Id, StringComparison.Ordinal))
          return null;
      }

      return new ImageService(_kernel, _driverId, imageId, _imageName, _tags[0]);
    }

    #endregion
  }
}
