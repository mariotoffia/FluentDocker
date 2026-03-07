using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Services.Impl
{
  /// <summary>
  /// Compose service implementation using kernel and driver.
  /// </summary>
  public class ComposeService : IComposeService
  {
    private readonly FluentDockerKernel _kernel;
    private readonly string _driverId;
    private readonly List<string> _composeFiles;
    private readonly string _projectName;
    private readonly bool _removeVolumes;
    private readonly bool _removeImages;
    private readonly Dictionary<string, Func<IServiceAsync, Task>> _hooks = new Dictionary<string, Func<IServiceAsync, Task>>();
    private ServiceRunningState _state = ServiceRunningState.Running;

    public ComposeService(
        FluentDockerKernel kernel,
        string driverId,
        List<string> composeFiles,
        string projectName,
        bool removeVolumes = false,
        bool removeImages = false)
    {
      _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
      _driverId = driverId ?? throw new ArgumentNullException(nameof(driverId));
      _composeFiles = composeFiles ?? throw new ArgumentNullException(nameof(composeFiles));
      _projectName = projectName ?? throw new ArgumentNullException(nameof(projectName));
      _removeVolumes = removeVolumes;
      _removeImages = removeImages;
    }

    public string Name => _projectName;
    public ServiceRunningState State => _state;
    public FluentDockerKernel Kernel => _kernel;
    public string DriverId => _driverId;
    public string ProjectName => _projectName;
    public IReadOnlyList<string> ComposeFiles => _composeFiles;

    public event ServiceDelegates.StateChange StateChange;

    public async Task<IList<ComposeServiceInfo>> ListServicesAsync(CancellationToken cancellationToken = default)
    {
      var driver = _kernel.SysCtl<IComposeDriver>(_driverId);
      var context = new DriverContext(_driverId);

      var config = new ComposeListConfig
      {
        ComposeFiles = _composeFiles,
        ProjectName = _projectName
      };

      var response = await driver.ListAsync(context, config, cancellationToken);

      if (!response.Success)
      {
        throw new DriverException(
            $"Failed to list compose services for project '{_projectName}': {response.Error}",
            response.ErrorCode,
            response.ErrorContext);
      }

      return response.Data;
    }

    public async Task<string> GetLogsAsync(bool follow = false, CancellationToken cancellationToken = default)
    {
      var driver = _kernel.SysCtl<IComposeDriver>(_driverId);
      var context = new DriverContext(_driverId);

      var config = new ComposeLogsConfig
      {
        ComposeFiles = _composeFiles,
        ProjectName = _projectName,
        Follow = follow
      };

      var response = await driver.GetLogsAsync(context, config, cancellationToken);

      if (!response.Success)
      {
        throw new DriverException(
            $"Failed to get logs for compose project '{_projectName}': {response.Error}",
            response.ErrorCode,
            response.ErrorContext);
      }

      return response.Data;
    }

    public async Task<string> ExecuteAsync(string service, string[] command, CancellationToken cancellationToken = default)
    {
      var driver = _kernel.SysCtl<IComposeDriver>(_driverId);
      var context = new DriverContext(_driverId);

      var config = new ComposeExecConfig
      {
        ComposeFiles = _composeFiles,
        ProjectName = _projectName,
        Service = service,
        Command = command
      };

      var response = await driver.ExecuteAsync(context, config, cancellationToken);

      if (!response.Success)
      {
        throw new DriverException(
            $"Failed to execute command in service '{service}' for project '{_projectName}': {response.Error}",
            response.ErrorCode,
            response.ErrorContext);
      }

      return response.Data;
    }

    public async Task ScaleAsync(string service, int replicas, CancellationToken cancellationToken = default)
    {
      var driver = _kernel.SysCtl<IComposeDriver>(_driverId);
      var context = new DriverContext(_driverId);

      var config = new ComposeScaleConfig
      {
        ComposeFiles = _composeFiles,
        ProjectName = _projectName,
        Scale = new Dictionary<string, int> { { service, replicas } }
      };

      var response = await driver.ScaleAsync(context, config, cancellationToken);

      if (!response.Success)
      {
        throw new DriverException(
            $"Failed to scale service '{service}' for project '{_projectName}': {response.Error}",
            response.ErrorCode,
            response.ErrorContext);
      }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
      var driver = _kernel.SysCtl<IComposeDriver>(_driverId);
      var context = new DriverContext(_driverId);

      var config = new ComposeFileConfig
      {
        ComposeFiles = _composeFiles,
        ProjectName = _projectName
      };

      var response = await driver.StartAsync(context, config, cancellationToken);

      if (!response.Success)
      {
        throw new DriverException(
            $"Failed to start compose project '{_projectName}': {response.Error}",
            response.ErrorCode,
            response.ErrorContext);
      }

      UpdateState(ServiceRunningState.Running);
      await ExecuteHooksAsync(ServiceRunningState.Running);
    }

    public async Task PauseAsync(CancellationToken cancellationToken = default)
    {
      var driver = _kernel.SysCtl<IComposeDriver>(_driverId);
      var context = new DriverContext(_driverId);

      var config = new ComposeFileConfig
      {
        ComposeFiles = _composeFiles,
        ProjectName = _projectName
      };

      var response = await driver.PauseAsync(context, config, cancellationToken);

      if (!response.Success)
      {
        throw new DriverException(
            $"Failed to pause compose project '{_projectName}': {response.Error}",
            response.ErrorCode,
            response.ErrorContext);
      }

      UpdateState(ServiceRunningState.Paused);
      await ExecuteHooksAsync(ServiceRunningState.Paused);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
      var driver = _kernel.SysCtl<IComposeDriver>(_driverId);
      var context = new DriverContext(_driverId);

      var config = new ComposeStopConfig
      {
        ComposeFiles = _composeFiles,
        ProjectName = _projectName
      };

      var response = await driver.StopAsync(context, config, cancellationToken);

      if (!response.Success)
      {
        throw new DriverException(
            $"Failed to stop compose project '{_projectName}': {response.Error}",
            response.ErrorCode,
            response.ErrorContext);
      }

      UpdateState(ServiceRunningState.Stopped);
      await ExecuteHooksAsync(ServiceRunningState.Stopped);
    }

    public async Task RemoveAsync(bool force = false, CancellationToken cancellationToken = default)
    {
      var driver = _kernel.SysCtl<IComposeDriver>(_driverId);
      var context = new DriverContext(_driverId);

      var config = new ComposeDownConfig
      {
        ComposeFiles = _composeFiles,
        ProjectName = _projectName,
        RemoveVolumes = _removeVolumes || force,
        RemoveImages = _removeImages ? "all" : null
      };

      var response = await driver.DownAsync(context, config, cancellationToken);

      if (!response.Success)
      {
        throw new DriverException(
            $"Failed to remove compose project '{_projectName}': {response.Error}",
            response.ErrorCode,
            response.ErrorContext);
      }

      UpdateState(ServiceRunningState.Removed);
      await ExecuteHooksAsync(ServiceRunningState.Removed);
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
    }

    public async ValueTask DisposeAsync()
    {
      try
      {
        await RemoveAsync(force: true);
      }
      catch
      {
      }
    }

    private void UpdateState(ServiceRunningState newState)
    {
      _state = newState;
      StateChange?.Invoke(this, new StateChangeEventArgs(this, newState));
    }

    private async Task ExecuteHooksAsync(ServiceRunningState state)
    {
      foreach (var hook in _hooks.Values)
      {
        try
        {
          await hook(this);
        }
        catch
        {
        }
      }
    }
  }
}

