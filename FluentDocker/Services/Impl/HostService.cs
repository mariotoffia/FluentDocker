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
  /// <inheritdoc />
  public partial class HostService : IHostService, IServiceCapabilities
  {
    // IServiceCapabilities
    bool IServiceCapabilities.CanStart => true;
    bool IServiceCapabilities.CanStop => false;
    bool IServiceCapabilities.CanPause => false;
    bool IServiceCapabilities.CanRemove => false;

    private readonly FluentDockerKernel _kernel;
    private readonly string _driverId;
    private readonly string _hostName;
    private readonly bool _isNative;
    private readonly bool _requireTls;
    private readonly ServiceRunningState _state = ServiceRunningState.Running;

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

    // Host services have a fixed Running state — event is required by IServiceAsync but never raised.
#pragma warning disable CS0067, CA1710
    public event ServiceDelegates.StateChange StateChange;
#pragma warning restore CS0067, CA1710

    #region System Information

    public async Task<SystemInfo> GetSystemInfoAsync(CancellationToken cancellationToken = default)
    {
      var driver = _kernel.SysCtl<ISystemDriver>(_driverId);
      var context = new DriverContext(_driverId);

      var response = await driver.GetInfoAsync(context, cancellationToken).ConfigureAwait(false);

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

      var response = await driver.GetVersionAsync(context, cancellationToken).ConfigureAwait(false);

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

      var response = await driver.PingAsync(context, cancellationToken).ConfigureAwait(false);
      return response.Success;
    }

    public async Task<DiskUsageInfo> GetDiskUsageAsync(CancellationToken cancellationToken = default)
    {
      var driver = _kernel.SysCtl<ISystemDriver>(_driverId);
      var context = new DriverContext(_driverId);

      var response = await driver.GetDiskUsageAsync(context, cancellationToken).ConfigureAwait(false);

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
      return await GetContainersAsync(false, null, cancellationToken).ConfigureAwait(false);
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
          ApplyContainerFilter(filter, kvp.Key, kvp.Value);
        }
      }

      var response = await driver.ListAsync(context, filter, cancellationToken).ConfigureAwait(false);

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
        // Discovered/borrowed containers: this library did not create them, so disposing
        // the wrapper must never stop or delete a user's running container.
        services.Add(new ContainerService(
            _kernel,
            _driverId,
            container.Id,
            container.Image,
            container.Name,
            stopOnDispose: false,
            deleteOnDispose: false));
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
        var (pullImage, pullTag) = ParseImagePullReference(image);
        var pullResponse = await imageDriver.PullAsync(pullContext, pullImage, pullTag, null, cancellationToken).ConfigureAwait(false);

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
        RestartPolicy = config.RestartPolicy,
        Labels = config.Labels ?? []
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
        createConfig.Volumes = [.. config.Volumes];
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
        createConfig.CpuQuota = config.CpuQuota.Value;
      }

      var response = await driver.CreateAsync(context, createConfig, cancellationToken).ConfigureAwait(false);

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
          config.Name ?? response.Data.Name ?? response.Data.Id,
          config.StopOnDispose,
          config.DeleteOnDispose,
          config.DeleteVolumeOnDispose,
          config.DeleteNamedVolumeOnDispose);
    }

    #endregion

    #region IServiceAsync Implementation

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
      return Task.CompletedTask;
    }

    public Task PauseAsync(CancellationToken cancellationToken = default)
    {
      throw new FluentDockerNotSupportedException("Docker hosts cannot be paused");
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
      throw new FluentDockerNotSupportedException("Native Docker hosts cannot be stopped");
    }

    public Task RemoveAsync(bool force = false, CancellationToken cancellationToken = default)
    {
      throw new FluentDockerNotSupportedException("Native Docker hosts cannot be removed");
    }

    public IServiceAsync AddHook(ServiceRunningState state, Func<IServiceAsync, Task> hook, string uniqueName = null)
    {
      throw new FluentDockerNotSupportedException("HostService has a fixed Running state and does not support hooks.");
    }

    public IServiceAsync RemoveHook(string uniqueName)
    {
      throw new FluentDockerNotSupportedException("HostService has a fixed Running state and does not support hooks.");
    }

    private int _disposed;

    public void Dispose()
    {
      if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        return;
      // Dispatched to the thread pool to avoid sync-over-async deadlocks.
      Task.Run(() => HostService.DisposeCoreAsync().AsTask()).GetAwaiter().GetResult();
      GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
      if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        return;
      await HostService.DisposeCoreAsync().ConfigureAwait(false);
      GC.SuppressFinalize(this);
    }

    private static async ValueTask DisposeCoreAsync()
    {
      await Task.CompletedTask.ConfigureAwait(false);
    }

    private static void ApplyContainerFilter(ContainerListFilter filter, string key, string value)
    {
      if (string.IsNullOrEmpty(key))
        return;

      switch (key?.ToLowerInvariant())
      {
        case "name":
          filter.Name = value;
          break;
        case "id":
          filter.Id = value;
          break;
        case "status":
          filter.Status = value;
          break;
        case "ancestor":
          filter.Ancestor = value;
          break;
        case "limit" when int.TryParse(value, out var limit):
          filter.Limit = limit;
          break;
        case "label":
          AddLabelFilter(filter, value);
          break;
        default:
          filter.Labels[key] = value;
          break;
      }
    }

    private static void AddLabelFilter(ContainerListFilter filter, string value)
    {
      if (string.IsNullOrEmpty(value))
        return;

      var separator = value.IndexOf('=');
      if (separator < 0)
      {
        filter.Labels[value] = string.Empty;
        return;
      }

      filter.Labels[value[..separator]] = value[(separator + 1)..];
    }

    private static (string Image, string Tag) ParseImagePullReference(string image)
    {
      if (string.IsNullOrEmpty(image) || image.Contains('@'))
        return (image, default!);

      var slash = image.LastIndexOf('/');
      var colon = image.LastIndexOf(':');
      if (colon > slash && colon < image.Length - 1)
        return (image[..colon], image[(colon + 1)..]);

      return (image, "latest");
    }

    #endregion
  }
}
