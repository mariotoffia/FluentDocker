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
  public partial class ComposeService : IComposeService, IServiceCapabilities
  {
    bool IServiceCapabilities.CanStart => true;
    bool IServiceCapabilities.CanStop => true;
    bool IServiceCapabilities.CanPause => true;
    bool IServiceCapabilities.CanRemove => true;

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
    private volatile ServiceRunningState _state = ServiceRunningState.Running;

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
        ServiceRunningState initialState = ServiceRunningState.Running)
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
      _ownedTempFiles = ownedTempFiles;
      _downOnDispose = downOnDispose;
      _state = initialState;
      _disposeCleanupTimeout =
          disposeCleanupTimeout ?? TimeSpan.FromMilliseconds(ContainerService.DefaultDisposeCleanupTimeoutMs);
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

    public IServiceAsync AddHook(ServiceRunningState state, Func<IServiceAsync, Task> hook, string uniqueName = null)
    {
      var name = uniqueName ?? Guid.NewGuid().ToString();
      _hooks[name] = (state, hook);
      return this;
    }

    public IServiceAsync RemoveHook(string uniqueName)
    {
      _hooks.TryRemove(uniqueName, out _);
      return this;
    }

    private int _disposed;
    private int _disposeCompleted;

    public void Dispose()
    {
      if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        return;
      try
      {
        // Dispatched to the thread pool to avoid sync-over-async deadlocks.
        Task.Run(() => DisposeCoreAsync().AsTask()).GetAwaiter().GetResult();
      }
      finally
      {
        Volatile.Write(ref _disposeCompleted, 1);
        GC.SuppressFinalize(this);
      }
    }

    public async ValueTask DisposeAsync()
    {
      if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        return;
      try
      {
        await DisposeCoreAsync().ConfigureAwait(false);
      }
      finally
      {
        Volatile.Write(ref _disposeCompleted, 1);
        GC.SuppressFinalize(this);
      }
    }

    private async ValueTask DisposeCoreAsync()
    {
      try
      {
        if (_downOnDispose)
        {
          using var cleanupCts = new CancellationTokenSource(_disposeCleanupTimeout);
          var removeTask = RemoveAsync(force: false, cleanupCts.Token);
          try
          {
            await removeTask.WaitAsync(cleanupCts.Token).ConfigureAwait(false);
          }
          catch (Exception ex)
          {
            _logger.LogWarning(ex, "ComposeService DisposeAsync failed");
            ObserveAbandonedCleanup(removeTask);
          }
        }
      }
      finally
      {
        DeleteOwnedTempFiles();
      }
    }

    private void DeleteOwnedTempFiles()
    {
      if (_ownedTempFiles is null)
        return;

      foreach (var path in _ownedTempFiles)
      {
        try
        {
          if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
            System.IO.File.Delete(path);
        }
        catch (Exception ex)
        {
          _logger.LogWarning(ex, "ComposeService failed to delete temp overlay file {Path}", path);
        }
      }
    }

    private static void ObserveAbandonedCleanup(Task task) =>
        _ = task.ContinueWith(
            static t => _ = t.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);

    private void UpdateState(ServiceRunningState newState)
    {
      if (Volatile.Read(ref _disposeCompleted) != 0 || _state == newState)
        return;

      _state = newState;
      var stateChange = StateChange;
      if (stateChange == null)
        return;

      var args = new StateChangeEventArgs(this, newState);
      foreach (ServiceDelegates.StateChange handler in stateChange.GetInvocationList())
      {
        try
        {
          handler(this, args);
        }
        catch (Exception ex)
        {
          _logger.LogError(ex, "ComposeService state change handler failed");
        }
      }
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
