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

    /// <summary>
    /// Creates a compose service bound to a driver and project.
    /// </summary>
    /// <param name="kernel">Kernel used to resolve compose driver ports.</param>
    /// <param name="driverId">Driver id registered in the kernel.</param>
    /// <param name="composeFiles">Compose files identifying the project.</param>
    /// <param name="projectName">Compose project name, or null when compose derives it.</param>
    /// <param name="removeVolumes">Whether owned volumes are removed during <c>compose down</c>.</param>
    /// <param name="removeImages">Whether owned images are removed during <c>compose down</c>.</param>
    /// <param name="ownedTempFiles">Temp compose files deleted when this service is disposed.</param>
    /// <param name="disposeCleanupTimeout">Maximum best-effort cleanup time during dispose.</param>
    /// <param name="downOnDispose">
    /// True when this service owns the project and may run <c>compose down</c>; false for borrowed
    /// handles from <c>ConnectToExisting</c>, which only release local resources.
    /// </param>
    /// <param name="initialState">Initial client-side lifecycle state.</param>
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
    /// <inheritdoc />
    public string Name => _projectName ?? "compose";

    /// <inheritdoc />
    public ServiceRunningState State => _state;

    /// <inheritdoc />
    public FluentDockerKernel Kernel => _kernel;

    /// <inheritdoc />
    public string DriverId => _driverId;

    /// <inheritdoc />
    public string ProjectName => _projectName;

    /// <inheritdoc />
    public IReadOnlyList<string> ComposeFiles => _composeFiles;

    /// <inheritdoc />
    public bool IsBorrowed => !_downOnDispose;

#pragma warning disable CA1710 // Delegate name 'StateChange' — intentional API design
    /// <inheritdoc />
    /// <remarks>
    /// State-change events publish optimistic transitions immediately: Start/Restart/Unpause raise
    /// a <see cref="ServiceRunningState.Running"/> event before the post-operation reconcile probe,
    /// so a Running event may be followed by Stopped/Unknown when reconciliation corrects the
    /// state. Lifecycle hooks registered via <see cref="AddHook"/> for Running fire only after
    /// reconciliation confirms the project is genuinely running.
    /// </remarks>
    public event ServiceDelegates.StateChange StateChange;
#pragma warning restore CA1710

    /// <summary>
    /// Lists services in this compose project.
    /// </summary>
    /// <remarks>
    /// Includes stopped and exited services so a fully stopped project is still observable.
    /// </remarks>
    public async Task<IList<ComposeServiceInfo>> ListServicesAsync(CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      ThrowIfDisposed();
      var driver = _kernel.SysCtl<IComposeDriver>(_driverId);
      var context = new DriverContext(_driverId);

      var config = new ComposeListConfig
      {
        ComposeFiles = _composeFiles,
        ProjectName = _projectName,
        All = true
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

    /// <inheritdoc />
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

    // Best-effort reconciliation after a mutating op: a `compose ps` corrects optimistic aggregate
    // state when some services crashed on start or did not stop (SVC-MAJ-6). If the probe itself
    // fails we keep the optimistic state rather than throwing — the mutation already succeeded.
    private async Task TryReconcileStateAsync(CancellationToken cancellationToken)
    {
      cancellationToken.ThrowIfCancellationRequested();
      try
      {
        await RefreshStateAsync(cancellationToken).ConfigureAwait(false);
      }
      catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
      {
        throw;
      }
      catch (Exception)
      {
        // Reconciliation is advisory; the optimistic state stands if `ps` is unavailable.
      }
    }

    /// <inheritdoc />
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
      var anyStarting = false;
      var allPaused = true;
      var allStopped = true;
      foreach (var s in services)
      {
        var state = s.State;
        if (!string.IsNullOrEmpty(state) &&
            state.Contains("running", StringComparison.OrdinalIgnoreCase))
        {
          anyRunning = true;
          break;
        }

        if (string.IsNullOrEmpty(state) ||
            (!state.Contains("stopped", StringComparison.OrdinalIgnoreCase) &&
             !state.Contains("exited", StringComparison.OrdinalIgnoreCase) &&
             !state.Contains("dead", StringComparison.OrdinalIgnoreCase)))
        {
          allStopped = false;
        }

        if (string.IsNullOrEmpty(state) ||
            !state.Contains("paused", StringComparison.OrdinalIgnoreCase))
        {
          allPaused = false;
        }

        if (!string.IsNullOrEmpty(state) &&
            state.Contains("restarting", StringComparison.OrdinalIgnoreCase))
        {
          anyStarting = true;
        }
      }

      UpdateState(anyRunning
          ? ServiceRunningState.Running
          : anyStarting
              ? ServiceRunningState.Starting
              : allStopped
                  ? ServiceRunningState.Stopped
                  : allPaused ? ServiceRunningState.Paused : ServiceRunningState.Unknown);
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      ThrowIfDisposed();
      if (_state == ServiceRunningState.Removed)
        throw new InvalidOperationException("Cannot start a removed compose project.");

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

        // `docker compose up/start` returns success even when a service crashes on boot, so
        // Running hooks must not fire until reconcile confirms the project is genuinely running
        // (S-H2, mirrors the RestartAsync fix at SVC-MAJ-3). Reconcile is best-effort: it
        // corrects the optimistic state but leaves it if the ps probe fails.
        UpdateState(ServiceRunningState.Running);
        await TryReconcileStateAsync(cancellationToken).ConfigureAwait(false);
        if (_state == ServiceRunningState.Running)
          await ExecuteHooksAsync(ServiceRunningState.Running).ConfigureAwait(false);
      }
      catch
      {
        await UpdateStateAndExecuteHooksAsync(ServiceRunningState.Unknown).ConfigureAwait(false);
        throw;
      }
    }

    /// <inheritdoc />
    public async Task PauseAsync(CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      ThrowIfDisposed();
      if (_state == ServiceRunningState.Removed)
        throw new InvalidOperationException("Cannot pause a removed compose project.");

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
        await UpdateStateAndExecuteHooksAsync(ServiceRunningState.Unknown).ConfigureAwait(false);
        throw;
      }
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      ThrowIfDisposed();
      if (_state == ServiceRunningState.Removed)
        return;

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

        // Same reordering as StartAsync (S-H2 / SVC-MAJ-3): reconcile before firing Stopped
        // hooks, so a stop that didn't actually take (e.g. a restart policy revived it) doesn't
        // fire Stopped hooks against a project that is still running.
        UpdateState(ServiceRunningState.Stopped);
        await TryReconcileStateAsync(cancellationToken).ConfigureAwait(false);
        if (_state == ServiceRunningState.Stopped)
          await ExecuteHooksAsync(ServiceRunningState.Stopped).ConfigureAwait(false);
      }
      catch
      {
        await UpdateStateAndExecuteHooksAsync(ServiceRunningState.Unknown).ConfigureAwait(false);
        throw;
      }
    }

    /// <inheritdoc />
    public Task RestartAsync(CancellationToken cancellationToken = default) =>
        RestartAsync(null, cancellationToken);

    /// <inheritdoc />
    public IServiceAsync AddHook(ServiceRunningState state, Func<IServiceAsync, Task> hook, string uniqueName = null)
    {
      ThrowIfDisposed();
      var name = uniqueName ?? Guid.NewGuid().ToString();
      _hooks[name] = (state, hook);
      return this;
    }

    /// <inheritdoc />
    public IServiceAsync RemoveHook(string uniqueName)
    {
      ThrowIfDisposed();
      _hooks.TryRemove(uniqueName, out _);
      return this;
    }

    private bool UpdateState(ServiceRunningState newState)
    {
      ServiceDelegates.StateChange stateChange;
      StateChangeEventArgs args;
      lock (_stateLock)
      {
        if (Volatile.Read(ref _disposeCompleted) != 0 || _state == newState)
          return false;

        _state = newState;
        stateChange = StateChange;
        if (stateChange == null)
          return true;

        args = new StateChangeEventArgs(this, newState);
      }

      StateChangeNotifier.Invoke(stateChange, args, _logger, "ComposeService");
      return true;
    }

    private async Task UpdateStateAndExecuteHooksAsync(ServiceRunningState newState)
    {
      if (UpdateState(newState))
        await ExecuteHooksAsync(newState).ConfigureAwait(false);
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
