#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Model.Models;

namespace FluentDocker.Common
{
  /// <summary>
  /// A process-wide, per-model async gate that serializes mutually-unsafe model
  /// lifecycle operations (pull / configure / load / unload / remove / tag / push) on the SAME model while letting
  /// DIFFERENT models proceed in parallel. Mirrors the per-machine lock idea used by
  /// <c>PodmanCliDriverPack</c> (a static <see cref="System.Collections.Concurrent.ConcurrentDictionary{TKey,TValue}"/>
  /// of <see cref="SemaphoreSlim"/> with <c>GetOrAdd</c> + <c>WaitAsync</c>/<c>Release</c>), but
  /// additionally ref-counts and evicts idle entries (see the remarks below) since models churn
  /// through far more distinct keys over a process lifetime than machine names do.
  /// </summary>
  /// <remarks>
  /// Public <c>ModelRunnerService</c> operations acquire this gate themselves. Composite builder
  /// work (inspect → optional pull → optional configure) acquires it once and calls no-reentrant
  /// core methods so the whole build-time sequence is atomic. Gates are reference-counted: the ref
  /// is reserved under a lock before the wait begins, so a gate is only ever evicted (and its
  /// semaphore disposed) once no caller holds or is queued on it. This keeps a long-lived process
  /// that churns through many distinct models from growing this table forever, while remaining
  /// safe from the eviction race a naive "CurrentCount == 1 ⇒ remove" check would introduce. This
  /// is only process-wide: parallel test or CI processes can still race each other.
  /// The key is the normalized model reference only and intentionally omits
  /// host / endpoint, so equal model names on different daemons share a gate.
  /// Raw string keys passed to <see cref="AcquireAsync(string, CancellationToken)"/>
  /// are caller-owned and are not registry-normalized.
  /// </remarks>
  public static class ModelOperationGate
  {
    [SuppressMessage("Reliability", "CA1001",
        Justification = "Gate does not own an independent disposal lifecycle; ReleaseRef disposes " +
            "Semaphore exactly once, under GatesLock, when RefCount reaches zero.")]
    private sealed class Gate
    {
      public readonly SemaphoreSlim Semaphore = new(1, 1);
      public int RefCount; // guarded by GatesLock
    }

    private static readonly Dictionary<string, Gate> Gates = new(StringComparer.Ordinal);
    private static readonly object GatesLock = new();

    /// <summary>
    /// Computes the serialization key for a model reference. Public static so the keying is
    /// unit-testable without internals access (the same surface convention as
    /// <c>PodmanCliDriverPack.MachineLockKey</c>).
    /// </summary>
    /// <param name="model">The model reference (may be null).</param>
    /// <returns>The canonical key; a null reference normalizes to the empty string.</returns>
    public static string KeyFor(ModelReference model) => model?.ToString() ?? string.Empty;

    /// <summary>
    /// Number of distinct model keys with a live gate right now (0 when idle). A gate is live
    /// while at least one caller holds or is waiting on it; once released with nobody left
    /// holding or queued, it is evicted and its semaphore disposed. For diagnostics/tests.
    /// </summary>
    public static int TrackedGateCount
    {
      get { lock (GatesLock) return Gates.Count; }
    }

    /// <summary>
    /// Whether <paramref name="key"/> currently has a live gate (held or with queued waiters).
    /// Per-key companion to <see cref="TrackedGateCount"/>: lets diagnostics/tests pin one key's
    /// eviction exactly, immune to unrelated keys churning the global count in parallel.
    /// </summary>
    /// <param name="key">The gate key (see <see cref="KeyFor(ModelReference)"/>); null normalizes to the empty string.</param>
    /// <returns><c>true</c> while the key's gate is live; <c>false</c> once evicted (or never acquired).</returns>
    public static bool IsTracked(string key)
    {
      lock (GatesLock)
        return Gates.ContainsKey(key ?? string.Empty);
    }

    /// <summary>
    /// Acquires the gate for <paramref name="key"/>, honoring cancellation while waiting.
    /// Dispose (preferably <c>await using</c>) the returned handle to release exactly once.
    /// </summary>
    /// <param name="key">The gate key (see <see cref="KeyFor(ModelReference)"/>).</param>
    /// <param name="cancellationToken">Cancels the wait for the gate.</param>
    /// <returns>A handle whose disposal releases the gate.</returns>
    public static async Task<IAsyncDisposable> AcquireAsync(string key, CancellationToken cancellationToken = default)
    {
      key ??= string.Empty;
      Gate gate;
      lock (GatesLock)
      {
        if (!Gates.TryGetValue(key, out gate!))
        {
          gate = new Gate();
          Gates[key] = gate;
        }
        gate.RefCount++; // reserve BEFORE waiting so it can't be evicted out from under us
      }
      try
      {
        await gate.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
      }
      catch
      {
        ReleaseRef(key, gate, acquired: false); // wait canceled/failed: undo the reservation, maybe evict
        throw;
      }
      return new Releaser(key, gate);
    }

    /// <summary>Acquires the gate for a model reference. See <see cref="AcquireAsync(string, CancellationToken)"/>.</summary>
    /// <param name="model">The model whose operations should serialize.</param>
    /// <param name="cancellationToken">Cancels the wait for the gate.</param>
    /// <returns>A handle whose disposal releases the gate.</returns>
    public static Task<IAsyncDisposable> AcquireAsync(ModelReference model, CancellationToken cancellationToken = default) =>
        AcquireAsync(KeyFor(model), cancellationToken);

    private static void ReleaseRef(string key, Gate gate, bool acquired)
    {
      lock (GatesLock)
      {
        if (acquired)
          gate.Semaphore.Release();
        if (--gate.RefCount == 0)
        {
          Gates.Remove(key);
          gate.Semaphore.Dispose(); // safe: RefCount 0 means no holder and no queued waiter
        }
      }
    }

    private sealed class Releaser : IAsyncDisposable
    {
      private readonly string _key;
      private readonly Gate _gate;
      private int _released;

      public Releaser(string key, Gate gate)
      {
        _key = key;
        _gate = gate;
      }

      public ValueTask DisposeAsync()
      {
        // Idempotent: release/evict at most once even if disposed twice.
        if (Interlocked.CompareExchange(ref _released, 1, 0) == 0)
          ReleaseRef(_key, _gate, acquired: true);
        return ValueTask.CompletedTask;
      }
    }
  }
}
