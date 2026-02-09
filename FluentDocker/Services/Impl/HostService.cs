using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Services.Impl
{
  /// <summary>
  /// Host service implementation using kernel and driver.
  /// </summary>
  public class HostService : IHostService
  {
    private readonly FluentDockerKernel _kernel;
    private readonly string _driverId;
    private readonly string _hostName;
    private readonly bool _isNative;
    private readonly bool _requireTls;
    private readonly Dictionary<string, Func<IServiceAsync, Task>> _hooks = new Dictionary<string, Func<IServiceAsync, Task>>();
    private ServiceRunningState _state = ServiceRunningState.Running;

    public HostService(
        FluentDockerKernel kernel,
        string driverId,
        string hostName,
        bool isNative = true,
        bool requireTls = false)
    {
      _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
      _driverId = driverId ?? throw new ArgumentNullException(nameof(driverId));
      _hostName = hostName ?? "native";
      _isNative = isNative;
      _requireTls = requireTls;
    }

    public string Name => _hostName;
    public ServiceRunningState State => _state;
    public FluentDockerKernel Kernel => _kernel;
    public string DriverId => _driverId;
    public bool IsNative => _isNative;
    public bool RequireTls => _requireTls;

    public event ServiceDelegates.StateChange StateChange;

    #region System Information

    public async Task<SystemInfo> GetSystemInfoAsync(CancellationToken cancellationToken = default)
    {
      var driver = _kernel.SysCtl<ISystemDriver>(_driverId);
      var context = new DriverContext(_driverId);

      var response = await driver.GetInfoAsync(context, cancellationToken);

      if (!response.Success)
      {
        throw new DriverException(
            $"Failed to get system info: {response.Error}",
            response.ErrorCode,
            response.ErrorContext);
      }

      return response.Data;
    }

    public async Task<VersionInfo> GetVersionAsync(CancellationToken cancellationToken = default)
    {
      var driver = _kernel.SysCtl<ISystemDriver>(_driverId);
      var context = new DriverContext(_driverId);

      var response = await driver.GetVersionAsync(context, cancellationToken);

      if (!response.Success)
      {
        throw new DriverException(
            $"Failed to get version info: {response.Error}",
            response.ErrorCode,
            response.ErrorContext);
      }

      return response.Data;
    }

    public async Task<bool> PingAsync(CancellationToken cancellationToken = default)
    {
      var driver = _kernel.SysCtl<ISystemDriver>(_driverId);
      var context = new DriverContext(_driverId);

      var response = await driver.PingAsync(context, cancellationToken);
      return response.Success;
    }

    public async Task<DiskUsageInfo> GetDiskUsageAsync(CancellationToken cancellationToken = default)
    {
      var driver = _kernel.SysCtl<ISystemDriver>(_driverId);
      var context = new DriverContext(_driverId);

      var response = await driver.GetDiskUsageAsync(context, cancellationToken);

      if (!response.Success)
      {
        throw new DriverException(
            $"Failed to get disk usage: {response.Error}",
            response.ErrorCode,
            response.ErrorContext);
      }

      return response.Data;
    }

    #endregion

    #region Container Management

    public async Task<IList<IContainerService>> GetRunningContainersAsync(CancellationToken cancellationToken = default)
    {
      return await GetContainersAsync(false, null, cancellationToken);
    }

    public async Task<IList<IContainerService>> GetContainersAsync(
        bool all = true,
        IDictionary<string, string> filters = null,
        CancellationToken cancellationToken = default)
    {
      var driver = _kernel.SysCtl<IContainerDriver>(_driverId);
      var context = new DriverContext(_driverId);

      var filter = new ContainerListFilter { All = all };
      if (filters != null)
      {
        foreach (var kvp in filters)
        {
          filter.Labels[kvp.Key] = kvp.Value;
        }
      }

      var response = await driver.ListAsync(context, filter, cancellationToken);

      if (!response.Success)
      {
        throw new DriverException(
            $"Failed to list containers: {response.Error}",
            response.ErrorCode,
            response.ErrorContext);
      }

      var services = new List<IContainerService>();
      foreach (var container in response.Data)
      {
        services.Add(new ContainerService(
            _kernel,
            _driverId,
            container.Id,
            container.Image,
            container.Name));
      }

      return services;
    }

    public async Task<IContainerService> CreateContainerAsync(
        string image,
        ContainerCreateOptions options = null,
        CancellationToken cancellationToken = default)
    {
      options ??= new ContainerCreateOptions();

      if (options.ForcePull)
      {
        var imageDriver = _kernel.SysCtl<IImageDriver>(_driverId);
        var pullContext = new DriverContext(_driverId);
        var pullResponse = await imageDriver.PullAsync(pullContext, image, "latest", null, cancellationToken);

        if (!pullResponse.Success)
        {
          throw new DriverException(
              $"Failed to pull image '{image}': {pullResponse.Error}",
              pullResponse.ErrorCode,
              pullResponse.ErrorContext);
        }
      }

      var driver = _kernel.SysCtl<IContainerDriver>(_driverId);
      var context = new DriverContext(_driverId);

      var config = new ContainerCreateConfig
      {
        Image = image,
        Name = options.Name,
        Command = options.Command,
        WorkingDirectory = options.WorkingDir,
        User = options.User,
        Privileged = options.Privileged,
        Labels = options.Labels ?? new Dictionary<string, string>()
      };

      if (options.Environment?.Count > 0)
      {
        config.Environment = options.Environment;
      }

      if (options.Ports?.Count > 0)
      {
        config.PortBindings = options.Ports;
      }

      if (options.Volumes?.Count > 0)
      {
        config.Volumes = options.Volumes
            .ToDictionary(v => v.Split(':').First(), v => v.Contains(':') ? v.Split(':').Last() : v);
      }

      if (!string.IsNullOrEmpty(options.Network))
      {
        config.NetworkMode = options.Network;
      }

      if (options.MemoryLimit.HasValue)
      {
        config.MemoryLimit = options.MemoryLimit.Value;
      }

      if (options.CpuQuota.HasValue)
      {
        config.CpuShares = options.CpuQuota.Value;
      }

      var response = await driver.CreateAsync(context, config, cancellationToken);

      if (!response.Success)
      {
        throw new DriverException(
            $"Failed to create container from image '{image}': {response.Error}",
            response.ErrorCode,
            response.ErrorContext);
      }

      return new ContainerService(
          _kernel,
          _driverId,
          response.Data.Id,
          image,
          options.Name ?? response.Data.Name ?? response.Data.Id);
    }

    #endregion

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

    #region IServiceAsync Implementation

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
      return Task.CompletedTask;
    }

    public Task PauseAsync(CancellationToken cancellationToken = default)
    {
      throw new NotSupportedException("Docker hosts cannot be paused");
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
      throw new NotSupportedException("Native Docker hosts cannot be stopped");
    }

    public Task RemoveAsync(bool force = false, CancellationToken cancellationToken = default)
    {
      throw new NotSupportedException("Native Docker hosts cannot be removed");
    }

    public IServiceAsync AddHook(ServiceRunningState state, Func<IServiceAsync, Task> hook, string uniqueName = null)
    {
      var name = uniqueName ?? Guid.NewGuid().ToString();
      _hooks[name] = hook;
      return this;
    }

    public IServiceAsync RemoveHook(string uniqueName)
    {
      _hooks.Remove(uniqueName);
      return this;
    }

    void IService.Start() => StartAsync().GetAwaiter().GetResult();
    void IService.Pause() => PauseAsync().GetAwaiter().GetResult();
    void IService.Stop() => StopAsync().GetAwaiter().GetResult();
    void IService.Remove(bool force) => RemoveAsync(force).GetAwaiter().GetResult();

    IService IService.AddHook(ServiceRunningState state, Action<IService> hook, string uniqueName)
    {
      return AddHook(state, async service => hook(service), uniqueName);
    }

    IService IService.RemoveHook(string uniqueName)
    {
      RemoveHook(uniqueName);
      return this;
    }

    public void Dispose()
    {
#if NETSTANDARD2_0
            DisposeAsync().GetAwaiter().GetResult();
#else
      DisposeAsync().AsTask().GetAwaiter().GetResult();
#endif
    }

#if NETSTANDARD2_0
        public async Task DisposeAsync()
#else
    public async ValueTask DisposeAsync()
#endif
    {
      await Task.CompletedTask;
    }

    #endregion
  }
}

