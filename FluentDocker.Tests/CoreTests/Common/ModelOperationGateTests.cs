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

      // A second acquisition of the SAME key must not complete while the first is held.
      var second = ModelOperationGate.AcquireAsync(key, ct);
      var raced = await Task.WhenAny(second, Task.Delay(TimeSpan.FromMilliseconds(200), ct));
      Assert.NotSame(second, raced);
      Assert.False(second.IsCompleted);

      // Releasing the first lets the second proceed.
      await first.DisposeAsync();
      var handle = await second.WaitAsync(TimeSpan.FromSeconds(5), ct);
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

      // If the double-dispose had over-released, the gate would permit two concurrent holders.
      var a = await ModelOperationGate.AcquireAsync(key, ct);
      var b = ModelOperationGate.AcquireAsync(key, ct);
      var raced = await Task.WhenAny(b, Task.Delay(TimeSpan.FromMilliseconds(200), ct));
      Assert.NotSame(b, raced);
      Assert.False(b.IsCompleted);

      await a.DisposeAsync();
      var second = await b.WaitAsync(TimeSpan.FromSeconds(5), ct);
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
  }
}
