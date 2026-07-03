using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Model.Models;

namespace FluentDocker.Common
{
  /// <summary>
  /// A process-wide, per-model async gate that serializes mutually-unsafe model
  /// lifecycle operations (pull / configure / load / unload) on the SAME model while letting
  /// DIFFERENT models proceed in parallel. Mirrors the per-machine lock pattern used by
  /// <c>PodmanCliDriverPack</c> (a static <see cref="ConcurrentDictionary{TKey,TValue}"/>
  /// of <see cref="SemaphoreSlim"/> with <c>GetOrAdd</c> + <c>WaitAsync</c>/<c>Release</c>).
  /// </summary>
  /// <remarks>
  /// Public <c>ModelRunnerService</c> operations acquire this gate themselves. Composite builder
  /// work (inspect → optional pull → optional configure) acquires it once and calls no-reentrant
  /// core methods so the whole build-time sequence is atomic. The semaphores live for the process
  /// lifetime (never disposed), like the Podman machine locks.
  /// </remarks>
  public static class ModelOperationGate
  {
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.Ordinal);

    /// <summary>
    /// Computes the serialization key for a model reference. Public static so the keying is
    /// unit-testable without internals access (the same surface convention as
    /// <c>PodmanCliDriverPack.MachineLockKey</c>).
    /// </summary>
    /// <param name="model">The model reference (may be null).</param>
    /// <returns>The canonical key; a null reference normalizes to the empty string.</returns>
    public static string KeyFor(ModelReference model) => model?.ToString() ?? string.Empty;

    /// <summary>
    /// Acquires the gate for <paramref name="key"/>, honoring cancellation while waiting.
    /// Dispose (preferably <c>await using</c>) the returned handle to release exactly once.
    /// </summary>
    /// <param name="key">The gate key (see <see cref="KeyFor(ModelReference)"/>).</param>
    /// <param name="cancellationToken">Cancels the wait for the gate.</param>
    /// <returns>A handle whose disposal releases the gate.</returns>
    public static async Task<IAsyncDisposable> AcquireAsync(string key, CancellationToken cancellationToken = default)
    {
      var gate = Gates.GetOrAdd(key ?? string.Empty, _ => new SemaphoreSlim(1, 1));
      await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
      return new Releaser(gate);
    }

    /// <summary>Acquires the gate for a model reference. See <see cref="AcquireAsync(string, CancellationToken)"/>.</summary>
    /// <param name="model">The model whose operations should serialize.</param>
    /// <param name="cancellationToken">Cancels the wait for the gate.</param>
    /// <returns>A handle whose disposal releases the gate.</returns>
    public static Task<IAsyncDisposable> AcquireAsync(ModelReference model, CancellationToken cancellationToken = default) =>
        AcquireAsync(KeyFor(model), cancellationToken);

    private sealed class Releaser : IAsyncDisposable
    {
      private readonly SemaphoreSlim _gate;
      private int _released;

      public Releaser(SemaphoreSlim gate) => _gate = gate;

      public ValueTask DisposeAsync()
      {
        // Idempotent: release the semaphore at most once even if disposed twice.
        if (Interlocked.CompareExchange(ref _released, 1, 0) == 0)
          _gate.Release();
        return ValueTask.CompletedTask;
      }
    }
  }
}
