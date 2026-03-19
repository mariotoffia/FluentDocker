using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;

#pragma warning disable CS0618 // IService obsolete — intentional usage

namespace FluentDocker.Services.Impl
{
  /// <summary>
  /// Host service implementation using kernel and driver.
  /// </summary>
  public partial class HostService : IHostService
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
      ArgumentNullException.ThrowIfNull(kernel);
      ArgumentNullException.ThrowIfNull(driverId);
      _kernel = kernel;
      _driverId = driverId;
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

    // Host services have a fixed Running state — event is required by IService but never raised.
#pragma warning disable CS0067, CA1710
    public event ServiceDelegates.StateChange StateChange;
#pragma warning restore CS0067, CA1710

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
        ContainerCreateOptions config = null,
        CancellationToken cancellationToken = default)
    {
      config ??= new ContainerCreateOptions();

      if (config.ForcePull)
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

      var createConfig = new ContainerCreateConfig
      {
        Image = image,
        Name = config.Name,
        Command = config.Command,
        WorkingDirectory = config.WorkingDir,
        User = config.User,
        Privileged = config.Privileged,
        Labels = config.Labels ?? new Dictionary<string, string>()
      };

      if (config.Environment?.Count > 0)
      {
        createConfig.Environment = config.Environment;
      }

      if (config.Ports?.Count > 0)
      {
        createConfig.PortBindings = config.Ports;
      }

      if (config.Volumes?.Count > 0)
      {
        createConfig.Volumes = config.Volumes
            .ToDictionary(v => v.Split(':').First(), v => v.Contains(':') ? v.Split(':').Last() : v);
      }

      if (!string.IsNullOrEmpty(config.Network))
      {
        createConfig.NetworkMode = config.Network;
      }

      if (config.MemoryLimit.HasValue)
      {
        createConfig.MemoryLimit = config.MemoryLimit.Value;
      }

      if (config.CpuQuota.HasValue)
      {
        createConfig.CpuShares = config.CpuQuota.Value;
      }

      var response = await driver.CreateAsync(context, createConfig, cancellationToken);

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
          config.Name ?? response.Data.Name ?? response.Data.Id);
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
      DisposeAsync().AsTask().GetAwaiter().GetResult();
      GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
      await Task.CompletedTask;
      GC.SuppressFinalize(this);
    }

    #endregion
  }
}

