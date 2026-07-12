using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Volumes;
using Microsoft.Extensions.Logging;

namespace FluentDocker.Services.Impl
{
  /// <inheritdoc />
  /// <remarks>
  /// Lifecycle transitions are individually atomic; a single service instance is not designed
  /// for concurrent lifecycle calls (Start/Stop/Remove/Dispose) from multiple threads.
  /// </remarks>
  public class VolumeService : IVolumeService, IServiceCapabilities
  {
    // IServiceCapabilities
    bool IServiceCapabilities.CanStart => false;
    bool IServiceCapabilities.CanStop => false;
    bool IServiceCapabilities.CanPause => false;
    bool IServiceCapabilities.CanRemove => true;
    bool IServiceCapabilities.CanHook => true;

    private readonly FluentDockerKernel _kernel;
    private readonly ILogger<VolumeService> _logger;
    private readonly string _driverId;
    private readonly string _volumeName;
    private readonly string _driver;
    private readonly bool _removeOnDispose;
    private readonly TimeSpan _disposeCleanupTimeout;
    private readonly ConcurrentDictionary<string, (ServiceRunningState State, Func<IServiceAsync, Task> Hook)> _hooks = [];
    private readonly object _stateLock = new();
    private volatile ServiceRunningState _state = ServiceRunningState.Running;

    /// <summary>
    /// Creates a volume service for an existing or newly-created volume.
    /// </summary>
    /// <param name="kernel">Kernel used to resolve volume driver ports.</param>
    /// <param name="driverId">Driver id registered in the kernel.</param>
    /// <param name="volumeName">Volume name used for driver operations.</param>
    /// <param name="driver">Volume driver name; defaults to <c>local</c> when null.</param>
    /// <param name="removeOnDispose">When true, dispose removes the owned volume.</param>
    /// <param name="disposeCleanupTimeout">Maximum best-effort remove time during dispose.</param>
    public VolumeService(
        FluentDockerKernel kernel,
        string driverId,
        string volumeName,
        string driver,
        bool removeOnDispose = false,
        TimeSpan? disposeCleanupTimeout = null)
    {
      ArgumentNullException.ThrowIfNull(kernel);
      ArgumentNullException.ThrowIfNull(driverId);
      ArgumentNullException.ThrowIfNull(volumeName);
      _kernel = kernel;
      _logger = kernel.LoggerFactory.CreateLogger<VolumeService>();
      _driverId = driverId;
      _volumeName = volumeName;
      _driver = driver ?? "local";
      _removeOnDispose = removeOnDispose;
      _disposeCleanupTimeout =
          disposeCleanupTimeout ?? TimeSpan.FromMilliseconds(ContainerService.DefaultDisposeCleanupTimeoutMs);
    }

    /// <inheritdoc />
    public string Name => _volumeName;

    /// <inheritdoc />
    public ServiceRunningState State => _state;

    /// <inheritdoc />
    public FluentDockerKernel Kernel => _kernel;

    /// <inheritdoc />
    public string DriverId => _driverId;

    /// <inheritdoc />
    public string VolumeName => _volumeName;

    /// <inheritdoc />
    public string Driver => _driver;

#pragma warning disable CA1710 // Delegate name 'StateChange' — intentional API design
    /// <inheritdoc />
    public event ServiceDelegates.StateChange StateChange;
#pragma warning restore CA1710

    /// <inheritdoc />
    public async Task<Volume> InspectAsync(CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      ThrowIfDisposed();
      var driver = _kernel.SysCtl<IVolumeDriver>(_driverId);
      var context = new DriverContext(_driverId);

      var response = await driver.InspectAsync(context, _volumeName, cancellationToken).ConfigureAwait(false);

      if (!response.Success)
      {
        throw new DriverException(
            $"Failed to inspect volume '{_volumeName}': {response.Error}",
            response.ErrorCode,
            response.ErrorContext);
      }

      return response.Data;
    }

    /// <summary>Volumes are already available when represented; start is a no-op.</summary>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      ThrowIfDisposed();
      return Task.CompletedTask;
    }

    /// <summary>Volumes are static storage resources; pause is not a supported operation.</summary>
    /// <exception cref="FluentDockerNotSupportedException">Always thrown.</exception>
    public Task PauseAsync(CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      ThrowIfDisposed();
      throw new FluentDockerNotSupportedException("Volumes cannot be paused");
    }

    /// <summary>Volumes are static storage resources; stop is not a supported operation.</summary>
    /// <exception cref="FluentDockerNotSupportedException">Always thrown; use <see cref="RemoveAsync"/> instead.</exception>
    public Task StopAsync(CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      ThrowIfDisposed();
      throw new FluentDockerNotSupportedException("Volumes cannot be stopped, use RemoveAsync instead");
    }

    /// <inheritdoc />
    public async Task RemoveAsync(bool force = false, CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      ThrowIfDisposed();
      if (State == ServiceRunningState.Removed)
        return;

      var driver = _kernel.SysCtl<IVolumeDriver>(_driverId);
      var context = new DriverContext(_driverId);

      try
      {
        UpdateState(ServiceRunningState.Removing);
        await ExecuteHooksAsync(ServiceRunningState.Removing).ConfigureAwait(false);

        var response = await driver.RemoveAsync(context, _volumeName, force, cancellationToken).ConfigureAwait(false);

        if (!response.Success)
        {
          if (IsVolumeAlreadyGone(response))
          {
            UpdateState(ServiceRunningState.Removed);
            await ExecuteHooksAsync(ServiceRunningState.Removed).ConfigureAwait(false);
            return;
          }

          throw new DriverException(
              $"Failed to remove volume '{_volumeName}': {response.Error}",
              response.ErrorCode,
              response.ErrorContext);
        }

        UpdateState(ServiceRunningState.Removed);
        await ExecuteHooksAsync(ServiceRunningState.Removed).ConfigureAwait(false);
      }
      catch
      {
        UpdateState(ServiceRunningState.Unknown);
        throw;
      }
    }

    // Docker CLI reports a missing volume as "<name> not found" with a generic RemoveFailed code
    // (only Podman/the API driver set the typed NotFound). The "not found" substring is therefore
    // required for CLI remove idempotency, but must be anchored to the volume name — otherwise an
    // unrelated "volume driver plugin xyz not found" would be mis-read as already-gone.
    private bool IsVolumeAlreadyGone(CommandResponse<Unit> response) =>
        response.ErrorCode == ErrorCodes.Volume.NotFound ||
        response.Error?.Contains("no such volume", StringComparison.OrdinalIgnoreCase) == true ||
        (response.Error?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true &&
         response.Error.Contains(_volumeName, StringComparison.OrdinalIgnoreCase));

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

    private int _disposed;
    private int _disposeCompleted;

    /// <inheritdoc />
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

    /// <inheritdoc />
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
      if (!_removeOnDispose)
        return;

      using var cleanupCts = new CancellationTokenSource(_disposeCleanupTimeout);
      var removeTask = RemoveAsync(force: true, cleanupCts.Token);
      try
      {
        await removeTask.WaitAsync(cleanupCts.Token).ConfigureAwait(false);
      }
      catch (Exception ex)
      {
        _logger.LogWarning(ex, "VolumeService DisposeAsync failed");
        ObserveAbandonedCleanup(removeTask);
      }
    }

    private static void ObserveAbandonedCleanup(Task task) =>
        _ = task.ContinueWith(
            static t => _ = t.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeCompleted) != 0, this);

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

      StateChangeNotifier.Invoke(stateChange, args, _logger, "VolumeService");
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
          _logger.LogError(ex, "VolumeService hook execution failed");
        }
      }
    }
  }
}
