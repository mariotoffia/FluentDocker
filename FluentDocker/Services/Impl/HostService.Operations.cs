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
      var driver = _kernel.SysCtl<IImageDriver>(_driverId);
      var context = new DriverContext(_driverId);

      filter ??= new ImageListFilter { All = all };

      var response = await driver.ListAsync(context, filter, cancellationToken);

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
        var tag = image.RepoTags?.FirstOrDefault()?.Split(':').LastOrDefault() ?? "latest";
        var repo = image.RepoTags?.FirstOrDefault()?.Split(':').FirstOrDefault();

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
      var driver = _kernel.SysCtl<IImageDriver>(_driverId);
      var context = new DriverContext(_driverId);

      var response = await driver.PullAsync(context, image, tag, progress, cancellationToken);

      if (!response.Success)
      {
        throw new DriverException(
            $"Failed to pull image '{image}:{tag}': {response.Error}",
            response.ErrorCode,
            response.ErrorContext);
      }

      var inspectResponse = await driver.InspectAsync(context, $"{image}:{tag}", cancellationToken);

      if (!inspectResponse.Success)
      {
        throw new DriverException(
            $"Failed to inspect pulled image '{image}:{tag}': {inspectResponse.Error}",
            inspectResponse.ErrorCode,
            inspectResponse.ErrorContext);
      }

      return new ImageService(
          _kernel,
          _driverId,
          inspectResponse.Data.Id,
          image,
          tag);
    }

    public async Task<IImageService> BuildImageAsync(
        ImageBuildConfig config,
        IProgress<ImageBuildProgress> progress = null,
        CancellationToken cancellationToken = default)
    {
      var driver = _kernel.SysCtl<IImageDriver>(_driverId);
      var context = new DriverContext(_driverId);

      var response = await driver.BuildAsync(context, config, progress, cancellationToken);

      if (!response.Success)
      {
        throw new DriverException(
            $"Failed to build image: {response.Error}",
            response.ErrorCode,
            response.ErrorContext);
      }

      var tag = config.Tags?.FirstOrDefault();
      var tagParts = tag?.Split(':');

      return new ImageService(
          _kernel,
          _driverId,
          response.Data.ImageId,
          tagParts?.FirstOrDefault(),
          tagParts?.LastOrDefault() ?? "latest");
    }

    #endregion

    #region Network Management

    public async Task<IList<INetworkService>> GetNetworksAsync(CancellationToken cancellationToken = default)
    {
      var driver = _kernel.SysCtl<INetworkDriver>(_driverId);
      var context = new DriverContext(_driverId);

      var response = await driver.ListAsync(context, null, cancellationToken);

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
      var driver = _kernel.SysCtl<INetworkDriver>(_driverId);
      var context = new DriverContext(_driverId);

      config ??= new NetworkCreateConfig();

      var driverConfig = new Drivers.NetworkCreateConfig
      {
        Name = name,
        Driver = config.Driver,
        Internal = config.Internal,
        EnableIPv6 = config.EnableIPv6,
        Labels = config.Labels ?? new Dictionary<string, string>(),
        Options = config.Options ?? new Dictionary<string, string>()
      };

      var response = await driver.CreateAsync(context, driverConfig, cancellationToken);

      if (!response.Success)
      {
        throw new DriverException(
            $"Failed to create network '{name}': {response.Error}",
            response.ErrorCode,
            response.ErrorContext);
      }

      return new NetworkService(
          _kernel,
          _driverId,
          response.Data.Id,
          name);
    }

    #endregion

    #region Volume Management

    public async Task<IList<IVolumeService>> GetVolumesAsync(CancellationToken cancellationToken = default)
    {
      var driver = _kernel.SysCtl<IVolumeDriver>(_driverId);
      var context = new DriverContext(_driverId);

      var response = await driver.ListAsync(context, null, cancellationToken);

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
      var volumeDriver = _kernel.SysCtl<IVolumeDriver>(_driverId);
      var context = new DriverContext(_driverId);

      var config = new VolumeCreateConfig
      {
        Name = name,
        Driver = driver,
        Labels = labels != null ? new Dictionary<string, string>(labels) : new Dictionary<string, string>(),
        DriverOpts = options != null ? new Dictionary<string, string>(options) : new Dictionary<string, string>()
      };

      var response = await volumeDriver.CreateAsync(context, config, cancellationToken);

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
      var driver = _kernel.SysCtl<ISystemDriver>(_driverId);
      var context = new DriverContext(_driverId);

      var response = await driver.PruneAsync(context, config, cancellationToken);

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
  }
}
