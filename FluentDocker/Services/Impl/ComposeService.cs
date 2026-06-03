using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using Microsoft.Extensions.Logging;

namespace FluentDocker.Services.Impl
{
  /// <summary>
  /// Compose service implementation using kernel and driver.
  /// </summary>
  public class ComposeService : IComposeService, IServiceCapabilities
  {
    // IServiceCapabilities
    bool IServiceCapabilities.CanStart => true;
    bool IServiceCapabilities.CanStop => true;
    bool IServiceCapabilities.CanPause => false;
    bool IServiceCapabilities.CanRemove => true;

    private readonly FluentDockerKernel _kernel;
    private readonly ILogger<ComposeService> _logger;
    private readonly string _driverId;
    private readonly List<string> _composeFiles;
    private readonly string _projectName;
    private readonly bool _removeVolumes;
    private readonly bool _removeImages;
    private readonly Dictionary<string, Func<IServiceAsync, Task>> _hooks = [];
    private ServiceRunningState _state = ServiceRunningState.Running;

    public ComposeService(
        FluentDockerKernel kernel,
        string driverId,
        List<string> composeFiles,
        string projectName,
        bool removeVolumes = false,
        bool removeImages = false)
    {
      ArgumentNullException.ThrowIfNull(kernel);
      ArgumentNullException.ThrowIfNull(driverId);
      ArgumentNullException.ThrowIfNull(composeFiles);
      ArgumentNullException.ThrowIfNull(projectName);
      _kernel = kernel;
      _logger = kernel.LoggerFactory.CreateLogger<ComposeService>();
      _driverId = driverId;
      _composeFiles = composeFiles;
      _projectName = projectName;
      _removeVolumes = removeVolumes;
      _removeImages = removeImages;
    }

    public string Name => _projectName;
    public ServiceRunningState State => _state;
    public FluentDockerKernel Kernel => _kernel;
    public string DriverId => _driverId;
    public string ProjectName => _projectName;
    public IReadOnlyList<string> ComposeFiles => _composeFiles;

#pragma warning disable CA1710 // Delegate name 'StateChange' — intentional API design
    public event ServiceDelegates.StateChange StateChange;
#pragma warning restore CA1710

    public async Task<IList<ComposeServiceInfo>> ListServicesAsync(CancellationToken cancellationToken = default)
    {
      var driver = _kernel.SysCtl<IComposeDriver>(_driverId);
      var context = new DriverContext(_driverId);

      var config = new ComposeListConfig
      {
        ComposeFiles = _composeFiles,
        ProjectName = _projectName
      };

      var response = await driver.ListAsync(context, config, cancellationToken).ConfigureAwait(false);

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

      var response = await driver.GetLogsAsync(context, config, cancellationToken).ConfigureAwait(false);

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

      var response = await driver.ExecuteAsync(context, config, cancellationToken).ConfigureAwait(false);

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

      var response = await driver.ScaleAsync(context, config, cancellationToken).ConfigureAwait(false);

      if (!response.Success)
      {
        throw new DriverException(
            $"Failed to scale service '{service}' for project '{_projectName}': {response.Error}",
            response.ErrorCode,
            response.ErrorContext);
      }
    }

    public async Task RefreshStateAsync(CancellationToken cancellationToken = default)
    {
      var services = await ListServicesAsync(cancellationToken).ConfigureAwait(false);

      if (services == null || services.Count == 0)
      {
        UpdateState(ServiceRunningState.Unknown);
        return;
      }

      var anyRunning = false;
      foreach (var s in services)
      {
        if (!string.IsNullOrEmpty(s.State) &&
            s.State.Contains("running", StringComparison.OrdinalIgnoreCase))
        {
          anyRunning = true;
          break;
        }
      }

      UpdateState(anyRunning ? ServiceRunningState.Running : ServiceRunningState.Stopped);
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

      var response = await driver.StartAsync(context, config, cancellationToken).ConfigureAwait(false);

      if (!response.Success)
      {
        throw new DriverException(
            $"Failed to start compose project '{_projectName}': {response.Error}",
            response.ErrorCode,
            response.ErrorContext);
      }

      UpdateState(ServiceRunningState.Running);
      await ExecuteHooksAsync(ServiceRunningState.Running).ConfigureAwait(false);
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

      var response = await driver.PauseAsync(context, config, cancellationToken).ConfigureAwait(false);

      if (!response.Success)
      {
        throw new DriverException(
            $"Failed to pause compose project '{_projectName}': {response.Error}",
            response.ErrorCode,
            response.ErrorContext);
      }

      UpdateState(ServiceRunningState.Paused);
      await ExecuteHooksAsync(ServiceRunningState.Paused).ConfigureAwait(false);
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

      var response = await driver.StopAsync(context, config, cancellationToken).ConfigureAwait(false);

      if (!response.Success)
      {
        throw new DriverException(
            $"Failed to stop compose project '{_projectName}': {response.Error}",
            response.ErrorCode,
            response.ErrorContext);
      }

      UpdateState(ServiceRunningState.Stopped);
      await ExecuteHooksAsync(ServiceRunningState.Stopped).ConfigureAwait(false);
    }

    public Task RestartAsync(CancellationToken cancellationToken = default) =>
        RestartAsync(null, cancellationToken);

    public async Task RestartAsync(IEnumerable<string> services, CancellationToken cancellationToken = default)
    {
      var driver = _kernel.SysCtl<IComposeDriver>(_driverId);
      var context = new DriverContext(_driverId);

      var config = new ComposeRestartConfig
      {
        ComposeFiles = _composeFiles,
        ProjectName = _projectName,
        Services = services is null ? [] : [.. services]
      };

      var response = await driver.RestartAsync(context, config, cancellationToken).ConfigureAwait(false);

      if (!response.Success)
      {
        throw new DriverException(
            $"Failed to restart compose project '{_projectName}': {response.Error}",
            response.ErrorCode,
            response.ErrorContext);
      }

      UpdateState(ServiceRunningState.Running);
      await ExecuteHooksAsync(ServiceRunningState.Running).ConfigureAwait(false);
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

      var response = await driver.DownAsync(context, config, cancellationToken).ConfigureAwait(false);

      if (!response.Success)
      {
        throw new DriverException(
            $"Failed to remove compose project '{_projectName}': {response.Error}",
            response.ErrorCode,
            response.ErrorContext);
      }

      UpdateState(ServiceRunningState.Removed);
      await ExecuteHooksAsync(ServiceRunningState.Removed).ConfigureAwait(false);
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

    private int _disposed;

    public void Dispose()
    {
      if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        return;
      DisposeCoreAsync().AsTask().GetAwaiter().GetResult();
      GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
      if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        return;
      await DisposeCoreAsync().ConfigureAwait(false);
      GC.SuppressFinalize(this);
    }

    private async ValueTask DisposeCoreAsync()
    {
      try
      {
        await RemoveAsync(force: true).ConfigureAwait(false);
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "ComposeService DisposeAsync failed");
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
          await hook(this).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
          _logger.LogError(ex, "ComposeService hook execution failed");
        }
      }
    }
  }
}

