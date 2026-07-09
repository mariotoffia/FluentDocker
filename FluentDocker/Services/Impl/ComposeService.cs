using System;
using System.Collections.Concurrent;
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
  /// <inheritdoc />
  /// <remarks>
  /// Lifecycle transitions are individually atomic; a single service instance is not designed
  /// for concurrent lifecycle calls (Start/Stop/Remove/Dispose) from multiple threads.
  /// </remarks>
  public partial class ComposeService : IComposeService, IServiceCapabilities
  {
    private readonly FluentDockerKernel _kernel;
    private readonly ILogger<ComposeService> _logger;
    private readonly string _driverId;
    private readonly List<string> _composeFiles;
    private readonly string _projectName;
    private readonly bool _removeVolumes;
    private readonly bool _removeImages;
    private readonly IReadOnlyList<string> _ownedTempFiles;
    private readonly TimeSpan _disposeCleanupTimeout;
    private readonly ConcurrentDictionary<string, (ServiceRunningState State, Func<IServiceAsync, Task> Hook)> _hooks = [];
    private readonly bool _downOnDispose;
    private readonly object _stateLock = new();
    private volatile ServiceRunningState _state = ServiceRunningState.Stopped;

    public ComposeService(
        FluentDockerKernel kernel,
        string driverId,
        List<string> composeFiles,
        string projectName,
        bool removeVolumes = false,
        bool removeImages = false,
        IReadOnlyList<string> ownedTempFiles = null,
        TimeSpan? disposeCleanupTimeout = null,
        bool downOnDispose = true,
        ServiceRunningState initialState = ServiceRunningState.Stopped)
    {
      ArgumentNullException.ThrowIfNull(kernel);
      ArgumentNullException.ThrowIfNull(driverId);
      ArgumentNullException.ThrowIfNull(composeFiles);
      // Null projectName is legal (compose derives it from the project directory) as long as
      // compose files can identify the project for ps/logs/exec/down.
      if (projectName is null && composeFiles.Count == 0)
        throw new ArgumentException(
            "Either a project name or at least one compose file is required.", nameof(projectName));
      _kernel = kernel;
      _logger = kernel.LoggerFactory.CreateLogger<ComposeService>();
      _driverId = driverId;
      _composeFiles = [.. composeFiles];
      _projectName = projectName;
      _removeVolumes = removeVolumes;
      _removeImages = removeImages;
      _ownedTempFiles = ownedTempFiles;
      _downOnDispose = downOnDispose;
      _state = initialState;
      _disposeCleanupTimeout =
          disposeCleanupTimeout ?? TimeSpan.FromMilliseconds(ContainerService.DefaultDisposeCleanupTimeoutMs);
    }

    // ponytail: display-only fallback — compose derives the real name; Name is not used for lookups.
    public string Name => _projectName ?? "compose";
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
      cancellationToken.ThrowIfCancellationRequested();
      ThrowIfDisposed();
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
      cancellationToken.ThrowIfCancellationRequested();
      ThrowIfDisposed();
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
      cancellationToken.ThrowIfCancellationRequested();
      ThrowIfDisposed();
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
      cancellationToken.ThrowIfCancellationRequested();
      ThrowIfDisposed();
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
      cancellationToken.ThrowIfCancellationRequested();
      ThrowIfDisposed();
      // ponytail: preserve RemoveAsync's idempotency guard; ps-empty must not resurrect Removed.
      if (_state == ServiceRunningState.Removed)
        return;

      var services = await ListServicesAsync(cancellationToken).ConfigureAwait(false);

      if (services == null || services.Count == 0)
      {
        UpdateState(ServiceRunningState.Unknown);
        return;
      }

      var anyRunning = false;
      var allStopped = true;
      foreach (var s in services)
      {
        if (!string.IsNullOrEmpty(s.State) &&
            s.State.Contains("running", StringComparison.OrdinalIgnoreCase))
        {
          anyRunning = true;
          break;
        }

        if (string.IsNullOrEmpty(s.State) ||
            (!s.State.Contains("stopped", StringComparison.OrdinalIgnoreCase) &&
             !s.State.Contains("exited", StringComparison.OrdinalIgnoreCase) &&
             !s.State.Contains("dead", StringComparison.OrdinalIgnoreCase)))
        {
          allStopped = false;
        }
      }

      UpdateState(anyRunning
          ? ServiceRunningState.Running
          : allStopped ? ServiceRunningState.Stopped : ServiceRunningState.Unknown);
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      ThrowIfDisposed();
      var driver = _kernel.SysCtl<IComposeDriver>(_driverId);
      var context = new DriverContext(_driverId);

      var config = new ComposeFileConfig
      {
        ComposeFiles = _composeFiles,
        ProjectName = _projectName
      };

      try
      {
        UpdateState(ServiceRunningState.Starting);
        await ExecuteHooksAsync(ServiceRunningState.Starting).ConfigureAwait(false);

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
      catch
      {
        UpdateState(ServiceRunningState.Unknown);
        throw;
      }
    }

    public async Task PauseAsync(CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      ThrowIfDisposed();
      var driver = _kernel.SysCtl<IComposeDriver>(_driverId);
      var context = new DriverContext(_driverId);

      var config = new ComposeFileConfig
      {
        ComposeFiles = _composeFiles,
        ProjectName = _projectName
      };

      try
      {
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
      catch
      {
        UpdateState(ServiceRunningState.Unknown);
        throw;
      }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      ThrowIfDisposed();
      var driver = _kernel.SysCtl<IComposeDriver>(_driverId);
      var context = new DriverContext(_driverId);

      var config = new ComposeStopConfig
      {
        ComposeFiles = _composeFiles,
        ProjectName = _projectName
      };

      try
      {
        UpdateState(ServiceRunningState.Stopping);
        await ExecuteHooksAsync(ServiceRunningState.Stopping).ConfigureAwait(false);

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
      catch
      {
        UpdateState(ServiceRunningState.Unknown);
        throw;
      }
    }

    public Task RestartAsync(CancellationToken cancellationToken = default) =>
        RestartAsync(null, cancellationToken);

    public async Task RestartAsync(IEnumerable<string> services, CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      ThrowIfDisposed();
      var driver = _kernel.SysCtl<IComposeDriver>(_driverId);
      var context = new DriverContext(_driverId);

      var config = new ComposeRestartConfig
      {
        ComposeFiles = _composeFiles,
        ProjectName = _projectName,
        Services = services is null ? [] : [.. services]
      };

      try
      {
        var response = await driver.RestartAsync(context, config, cancellationToken).ConfigureAwait(false);

        if (!response.Success)
        {
          throw new DriverException(
              $"Failed to restart compose project '{_projectName}': {response.Error}",
              response.ErrorCode,
              response.ErrorContext);
        }

        if (config.Services.Count == 0)
          UpdateState(ServiceRunningState.Running);
        else
          await RefreshStateAsync(cancellationToken).ConfigureAwait(false);
        if (_state == ServiceRunningState.Running)
          await ExecuteHooksAsync(ServiceRunningState.Running).ConfigureAwait(false);
      }
      catch
      {
        UpdateState(ServiceRunningState.Unknown);
        throw;
      }
    }

    public IServiceAsync AddHook(ServiceRunningState state, Func<IServiceAsync, Task> hook, string uniqueName = null)
    {
      ThrowIfDisposed();
      var name = uniqueName ?? Guid.NewGuid().ToString();
      _hooks[name] = (state, hook);
      return this;
    }

    public IServiceAsync RemoveHook(string uniqueName)
    {
      ThrowIfDisposed();
      _hooks.TryRemove(uniqueName, out _);
      return this;
    }

    private void UpdateState(ServiceRunningState newState)
    {
      ServiceDelegates.StateChange stateChange;
      StateChangeEventArgs args;
      lock (_stateLock)
      {
        if (Volatile.Read(ref _disposeCompleted) != 0 || _state == newState)
          return;

        _state = newState;
        stateChange = StateChange;
        if (stateChange == null)
          return;

        args = new StateChangeEventArgs(this, newState);
      }

      StateChangeNotifier.Invoke(stateChange, args, _logger, "ComposeService");
    }

    private async Task ExecuteHooksAsync(ServiceRunningState state)
    {
      if (Volatile.Read(ref _disposeCompleted) != 0)
        return;

      foreach (var entry in _hooks.Values)
      {
        if (entry.State != state)
          continue;

        try
        {
          await entry.Hook(this).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
          _logger.LogError(ex, "ComposeService hook execution failed");
        }
      }
    }
  }
}
