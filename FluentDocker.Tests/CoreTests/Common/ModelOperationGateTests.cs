using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Model.Models;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Common
{
  /// <summary>
  /// Unit tests for <see cref="ModelOperationGate"/> (item 6): the per-model gate must
  /// serialize pull/load/unload of the SAME model while letting DIFFERENT models proceed
  /// in parallel. Keys are unique per test so the process-wide gate never races siblings.
  /// </summary>
  [Trait("Category", "Unit")]
  public class ModelOperationGateTests
  {
    private static string UniqueKey() => "ai/test-" + Guid.NewGuid().ToString("N");

    [Fact]
    public void KeyFor_NormalizesNullToEmpty_AndIsStableForSameReference()
    {
      Assert.Equal(string.Empty, ModelOperationGate.KeyFor(null!));

      var model = ModelReference.Parse("ai/smollm2");
      Assert.Equal(ModelOperationGate.KeyFor(model), ModelOperationGate.KeyFor(model));
      Assert.Equal(model.ToString(), ModelOperationGate.KeyFor(model));
    }

    [Fact]
    public async Task SameKey_SecondAcquire_BlocksUntilFirstReleases()
    {
      var key = UniqueKey();
      var ct = TestContext.Current.CancellationToken;

      var first = await ModelOperationGate.AcquireAsync(key, ct);

      // A second acquisition of the SAME key must wait and therefore honor cancellation.
      using var waitingCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
      var second = ModelOperationGate.AcquireAsync(key, waitingCts.Token);
      await waitingCts.CancelAsync();
      await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);

      await first.DisposeAsync();
      var handle = await ModelOperationGate.AcquireAsync(key, ct);
      await handle.DisposeAsync();
    }

    [Fact]
    public async Task DifferentKeys_DoNotBlockEachOther()
    {
      var ct = TestContext.Current.CancellationToken;
      var held = await ModelOperationGate.AcquireAsync(UniqueKey(), ct);

      // A DIFFERENT model's gate must be acquirable immediately even while the first is held.
      var other = await ModelOperationGate.AcquireAsync(UniqueKey(), ct).WaitAsync(TimeSpan.FromSeconds(5), ct);

      await other.DisposeAsync();
      await held.DisposeAsync();
    }

    [Fact]
    public async Task Release_IsIdempotent_DoubleDisposeDoesNotOverRelease()
    {
      var key = UniqueKey();
      var ct = TestContext.Current.CancellationToken;

      var handle = await ModelOperationGate.AcquireAsync(key, ct);
      await handle.DisposeAsync();
      await handle.DisposeAsync(); // second dispose must be a no-op (no extra Release)

      var a = await ModelOperationGate.AcquireAsync(key, ct);
      using var waitingCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
      var b = ModelOperationGate.AcquireAsync(key, waitingCts.Token);
      await waitingCts.CancelAsync();
      await Assert.ThrowsAnyAsync<OperationCanceledException>(() => b);

      await a.DisposeAsync();
      var second = await ModelOperationGate.AcquireAsync(key, ct);
      await second.DisposeAsync();
    }

    [Fact]
    public async Task Acquire_HonorsCancellationWhileWaiting()
    {
      var key = UniqueKey();
      var outerCt = TestContext.Current.CancellationToken;
      var held = await ModelOperationGate.AcquireAsync(key, outerCt);

      using var cts = CancellationTokenSource.CreateLinkedTokenSource(outerCt);
      var waiting = ModelOperationGate.AcquireAsync(key, cts.Token);
      cts.Cancel();

      await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await waiting);
      await held.DisposeAsync();
    }

    // --- Idle eviction (task-2 brief: ref-counted gate must not leak process-lifetime memory) ---
    // TrackedGateCount assertions are baseline-relative (captured at the top of each test), not
    // absolute zero: this static gate is process-wide and other test classes exercise it with
    // their own unique keys concurrently (xUnit parallelizes across test classes by default), so
    // an absolute-zero assertion would be flaky under suite-wide parallelism even though the fix
    // is correct. Baseline-relative assertions isolate this test's own keys from that noise.

    [Fact]
    public async Task Release_EvictsGate_AfterSequentialAcquireReleaseOfManyKeys()
    {
      var ct = TestContext.Current.CancellationToken;
      var baseline = ModelOperationGate.TrackedGateCount;

      for (var i = 0; i < 20; i++)
      {
        var handle = await ModelOperationGate.AcquireAsync(UniqueKey(), ct);
        await handle.DisposeAsync();
      }

      // Pre-fix this fails: nothing ever evicts, so the count grows by 20 instead of returning
      // to baseline.
      Assert.Equal(baseline, ModelOperationGate.TrackedGateCount);
    }

    [Fact]
    public async Task ConcurrentAcquires_SameKey_SerializeAndEvictWhenAllComplete()
    {
      var key = UniqueKey();
      var ct = TestContext.Current.CancellationToken;
      var baseline = ModelOperationGate.TrackedGateCount;
      var concurrent = 0;
      var maxObserved = 0;
      var maxLock = new object();

      async Task RunOnceAsync()
      {
        await using var handle = await ModelOperationGate.AcquireAsync(key, ct);
        var current = Interlocked.Increment(ref concurrent);
        lock (maxLock)
        {
          if (current > maxObserved)
          {
            maxObserved = current;
          }
        }
        await Task.Yield(); // give the scheduler a chance to run a concurrent waiter if the mutex is broken
        Interlocked.Decrement(ref concurrent);
      }

      var tasks = new Task[25];
      for (var i = 0; i < tasks.Length; i++)
      {
        tasks[i] = RunOnceAsync();
      }

      await Task.WhenAll(tasks);

      Assert.Equal(1, maxObserved);
      Assert.Equal(baseline, ModelOperationGate.TrackedGateCount);
    }

    [Fact]
    public async Task Release_WithWaiterQueued_DoesNotEvictUntilWaiterAlsoCompletes()
    {
      var key = UniqueKey();
      var ct = TestContext.Current.CancellationToken;
      var baseline = ModelOperationGate.TrackedGateCount;

      var a = await ModelOperationGate.AcquireAsync(key, ct);
      Assert.Equal(baseline + 1, ModelOperationGate.TrackedGateCount);

      // Not awaited: AcquireAsync runs synchronously up to its first real suspension point
      // (gate.Semaphore.WaitAsync, which cannot complete synchronously while A holds it), so by
      // the time this call returns, B's ref-count reservation has already been taken under the
      // lock — no Task.Yield/TCS needed to order this deterministically.
      var bTask = ModelOperationGate.AcquireAsync(key, ct);
      Assert.False(bTask.IsCompleted);
      Assert.Equal(baseline + 1, ModelOperationGate.TrackedGateCount);

      await a.DisposeAsync();
      // B still holds a reservation on the SAME gate: releasing A must not evict it out from
      // under B (the mutual-exclusion race the naive "CurrentCount == 1" fix would hit).
      Assert.Equal(baseline + 1, ModelOperationGate.TrackedGateCount);

      var b = await bTask;
      await b.DisposeAsync();
      Assert.Equal(baseline, ModelOperationGate.TrackedGateCount);
    }

    [Fact]
    public async Task Acquire_CanceledWhileWaiting_UndoesReservation_GateStillEvicts()
    {
      var key = UniqueKey();
      var ct = TestContext.Current.CancellationToken;
      var baseline = ModelOperationGate.TrackedGateCount;

      var a = await ModelOperationGate.AcquireAsync(key, ct);

      using var waitingCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
      var bTask = ModelOperationGate.AcquireAsync(key, waitingCts.Token);
      await waitingCts.CancelAsync();
      await Assert.ThrowsAnyAsync<OperationCanceledException>(() => bTask);

      // B's canceled reservation must be undone; A's is still live.
      Assert.Equal(baseline + 1, ModelOperationGate.TrackedGateCount);

      await a.DisposeAsync();
      Assert.Equal(baseline, ModelOperationGate.TrackedGateCount);
    }
  }
}
