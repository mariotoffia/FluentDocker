using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Services.Impl
{
  /// <summary>
  /// Host service — image, network, volume management and maintenance operations.
  /// </summary>
  public partial class HostService
  {
    #region Image Management

    public async Task<IList<IImageService>> GetImagesAsync(
        bool all = true,
        ImageListFilter filter = null,
        CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      ThrowIfDisposed();
      var driver = _kernel.SysCtl<IImageDriver>(_driverId);
      var context = new DriverContext(_driverId);

      filter ??= new ImageListFilter { All = all };

      var response = await driver.ListAsync(context, filter, cancellationToken).ConfigureAwait(false);

      if (!response.Success)
      {
        throw new DriverException(
            $"Failed to list images: {response.Error}",
            response.ErrorCode,
            response.ErrorContext);
      }

      var services = new List<IImageService>();
      foreach (var image in response.Data)
      {
        var (repo, tag) = ParseImagePullReference(image.RepoTags?.FirstOrDefault());

        services.Add(new ImageService(
            _kernel,
            _driverId,
            image.Id,
            repo,
            tag));
      }

      return services;
    }

    public async Task<IImageService> PullImageAsync(
        string image,
        string tag = "latest",
        IProgress<ImagePullProgress> progress = null,
        CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      ThrowIfDisposed();
      var driver = _kernel.SysCtl<IImageDriver>(_driverId);
      var context = new DriverContext(_driverId);
      var pullImage = image;
      var pullTag = tag;
      if (tag == "latest" && HasExplicitImageTag(image))
        (pullImage, pullTag) = ParseImagePullReference(image);

      var response = await driver.PullAsync(context, pullImage, pullTag, progress, cancellationToken).ConfigureAwait(false);

      if (!response.Success)
      {
        throw new DriverException(
            $"Failed to pull image '{pullImage}:{pullTag}': {response.Error}",
            response.ErrorCode,
            response.ErrorContext);
      }

      // A digest reference ("repo@sha256:...") must be inspected by the digest ref itself, not
      // "repo@sha256:...:latest" (which is malformed and fails to inspect).
      var digestSeparator = image.IndexOf('@');
      var isDigest = digestSeparator >= 0;
      var inspectRef = isDigest ? image : $"{pullImage}:{pullTag}";

      var inspectResponse = await driver.InspectAsync(context, inspectRef, cancellationToken).ConfigureAwait(false);

      if (!inspectResponse.Success)
      {
        throw new DriverException(
            $"Failed to inspect pulled image '{inspectRef}': {inspectResponse.Error}",
            inspectResponse.ErrorCode,
            inspectResponse.ErrorContext);
      }

      return new ImageService(
          _kernel,
          _driverId,
          inspectResponse.Data.Id,
          isDigest ? image[..digestSeparator] : pullImage,
          isDigest ? image[(digestSeparator + 1)..] : pullTag);
    }

    public async Task<IImageService> BuildImageAsync(
        ImageBuildConfig config,
        IProgress<ImageBuildProgress> progress = null,
        CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      ThrowIfDisposed();
      var driver = _kernel.SysCtl<IImageDriver>(_driverId);
      var context = new DriverContext(_driverId);

      var response = await driver.BuildAsync(context, config, progress, cancellationToken).ConfigureAwait(false);

      if (!response.Success)
      {
        throw new DriverException(
            $"Failed to build image: {response.Error}",
            response.ErrorCode,
            response.ErrorContext);
      }

      var (repo, tag) = ParseImagePullReference(config.Tags?.FirstOrDefault());

      return new ImageService(
          _kernel,
          _driverId,
          response.Data.ImageId,
          repo,
          tag);
    }

    #endregion

    #region Network Management

    public async Task<IList<INetworkService>> GetNetworksAsync(CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      ThrowIfDisposed();
      var driver = _kernel.SysCtl<INetworkDriver>(_driverId);
      var context = new DriverContext(_driverId);

      var response = await driver.ListAsync(context, null, cancellationToken).ConfigureAwait(false);

      if (!response.Success)
      {
        throw new DriverException(
            $"Failed to list networks: {response.Error}",
            response.ErrorCode,
            response.ErrorContext);
      }

      var services = new List<INetworkService>();
      foreach (var network in response.Data)
      {
        services.Add(new NetworkService(
            _kernel,
            _driverId,
            network.Id,
            network.Name));
      }

      return services;
    }

    public async Task<INetworkService> CreateNetworkAsync(
        string name,
        NetworkCreateConfig config = null,
        CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      ThrowIfDisposed();
      var driver = _kernel.SysCtl<INetworkDriver>(_driverId);
      var context = new DriverContext(_driverId);

      var createConfig = CloneNetworkCreateConfig(config);
      createConfig.Name = name ?? createConfig.Name;

      var response = await driver.CreateAsync(context, createConfig, cancellationToken).ConfigureAwait(false);

      if (!response.Success)
      {
        throw new DriverException(
            $"Failed to create network '{createConfig.Name}': {response.Error}",
            response.ErrorCode,
            response.ErrorContext);
      }

      return new NetworkService(
          _kernel,
          _driverId,
          response.Data.Id,
          createConfig.Name);
    }

    #endregion

    #region Volume Management

    public async Task<IList<IVolumeService>> GetVolumesAsync(CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      ThrowIfDisposed();
      var driver = _kernel.SysCtl<IVolumeDriver>(_driverId);
      var context = new DriverContext(_driverId);

      var response = await driver.ListAsync(context, null, cancellationToken).ConfigureAwait(false);

      if (!response.Success)
      {
        throw new DriverException(
            $"Failed to list volumes: {response.Error}",
            response.ErrorCode,
            response.ErrorContext);
      }

      var services = new List<IVolumeService>();
      foreach (var volume in response.Data)
      {
        services.Add(new VolumeService(
            _kernel,
            _driverId,
            volume.Name,
            volume.Driver));
      }

      return services;
    }

    public async Task<IVolumeService> CreateVolumeAsync(
        string name = null,
        string driver = "local",
        IDictionary<string, string> labels = null,
        IDictionary<string, string> options = null,
        CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      ThrowIfDisposed();
      var volumeDriver = _kernel.SysCtl<IVolumeDriver>(_driverId);
      var context = new DriverContext(_driverId);

      var config = new VolumeCreateConfig
      {
        Name = name,
        Driver = driver,
        Labels = labels != null ? new Dictionary<string, string>(labels) : [],
        DriverOpts = options != null ? new Dictionary<string, string>(options) : []
      };

      var response = await volumeDriver.CreateAsync(context, config, cancellationToken).ConfigureAwait(false);

      if (!response.Success)
      {
        throw new DriverException(
            $"Failed to create volume: {response.Error}",
            response.ErrorCode,
            response.ErrorContext);
      }

      return new VolumeService(
          _kernel,
          _driverId,
          response.Data.Name,
          response.Data.Driver);
    }

    #endregion

    #region Maintenance

    public async Task<SystemPruneResult> PruneAsync(
        SystemPruneConfig config = null,
        CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      ThrowIfDisposed();
      var driver = _kernel.SysCtl<ISystemDriver>(_driverId);
      var context = new DriverContext(_driverId);

      var response = await driver.PruneAsync(context, config, cancellationToken).ConfigureAwait(false);

      if (!response.Success)
      {
        throw new DriverException(
            $"Failed to prune system: {response.Error}",
            response.ErrorCode,
            response.ErrorContext);
      }

      return response.Data;
    }

    #endregion

    private static bool HasExplicitImageTag(string image)
    {
      if (string.IsNullOrEmpty(image) || image.Contains('@'))
        return false;

      var slash = image.LastIndexOf('/');
      var colon = image.LastIndexOf(':');
      return colon > slash && colon < image.Length - 1;
    }

    private static NetworkCreateConfig CloneNetworkCreateConfig(NetworkCreateConfig config)
    {
      if (config == null)
        return new NetworkCreateConfig();

      return new NetworkCreateConfig
      {
        Name = config.Name,
        Driver = config.Driver,
        Options = config.Options == null ? [] : new Dictionary<string, string>(config.Options),
        Subnet = config.Subnet,
        Gateway = config.Gateway,
        IpRange = config.IpRange,
        EnableIPv6 = config.EnableIPv6,
        Internal = config.Internal,
        Labels = config.Labels == null ? [] : new Dictionary<string, string>(config.Labels)
      };
    }
  }
}
