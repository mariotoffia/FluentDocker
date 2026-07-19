#nullable disable warnings
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Model.Containers;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Services.Impl
{
  /// <summary>
  /// Container service — inspect result caching and state synchronization. Split from the main
  /// <see cref="ContainerService"/> file purely to keep each source file within the
  /// repository's 500-line limit.
  /// </summary>
  public partial class ContainerService
  {
    // Short-lived inspect cache to avoid redundant API/CLI calls during wait polling.
    // Thread-safety: Single immutable record reference ensures atomic read/write
    // of both data and timestamp together, preventing torn reads.
    // The _cacheVersion counter prevents stale writes: if a state change occurs
    // while an InspectAsync is in-flight, the result is discarded rather than cached.
    private volatile InspectCacheEntry _inspectCacheEntry;
    private volatile int _cacheVersion;
    private int _inspectSequence;
    private int _lastAppliedInspectSequence;

    /// <summary>
    /// Immutable cache entry pairing inspect data with its timestamp.
    /// Using a single reference ensures atomic reads/writes.
    /// </summary>
    private sealed record InspectCacheEntry(Container Data, long Timestamp);

    /// <summary>
    /// Time-to-live in milliseconds for the InspectAsync result cache.
    /// </summary>
    public const long InspectCacheTtlMs = 500;

    /// <inheritdoc />
    public async Task<Container> InspectAsync(CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      ThrowIfDisposed();
      // Return cached result if still valid (reduces redundant calls during wait polling).
      // Single volatile reference read ensures data and timestamp are always consistent.
      var entry = _inspectCacheEntry;
      var now = _timeProvider.GetTimestamp();
      if (entry != null &&
          _timeProvider.GetElapsedTime(entry.Timestamp, now).TotalMilliseconds < InspectCacheTtlMs)
      {
        return entry.Data;
      }

      // Capture the cache version before the async call. If a state change
      // occurs during the fetch, the version will have incremented and we
      // must not store the now-stale result in the cache.
      var versionBefore = _cacheVersion;
      var inspectSequence = Interlocked.Increment(ref _inspectSequence);

      var driver = _kernel.SysCtl<IContainerDriver>(_driverId);
      var context = new DriverContext(_driverId);

      var response = await driver.InspectAsync(context, _containerId, cancellationToken).ConfigureAwait(false);

      if (!response.Success)
      {
        if (IsContainerAlreadyGone(response))
          await UpdateStateAndExecuteHooksAsync(ServiceRunningState.Removed).ConfigureAwait(false);
        throw new DriverException(
            $"Failed to inspect container '{_name}': {response.Error}",
            response.ErrorCode,
            response.ErrorContext);
      }

      await ApplyInspectResultIfVersionCurrentAsync(versionBefore, inspectSequence, response.Data)
          .ConfigureAwait(false);

      return response.Data;
    }

    private async Task ApplyInspectResultIfVersionCurrentAsync(int versionBefore, int inspectSequence, Container data)
    {
      ServiceDelegates.StateChange stateChange = null;
      StateChangeEventArgs args = null;
      ServiceRunningState? changedState = null;
      lock (_stateLock)
      {
        if (Volatile.Read(ref _disposeCompleted) != 0 ||
            versionBefore != _cacheVersion ||
            inspectSequence < _lastAppliedInspectSequence)
          return;

        _lastAppliedInspectSequence = inspectSequence;
        if (data?.State != null)
        {
          var newState = ParseInspectState(data.State);
          if (_state != newState)
          {
            _state = newState;
            changedState = newState;
            stateChange = StateChange;
            args = stateChange == null ? null : new StateChangeEventArgs(this, newState);
          }
        }

        _inspectCacheEntry = new InspectCacheEntry(data, _timeProvider.GetTimestamp());
      }

      if (stateChange != null)
        StateChangeNotifier.Invoke(stateChange, args, _logger, "ContainerService");

      if (changedState.HasValue)
        await ExecuteHooksAsync(changedState.Value).ConfigureAwait(false);
    }
  }
}
