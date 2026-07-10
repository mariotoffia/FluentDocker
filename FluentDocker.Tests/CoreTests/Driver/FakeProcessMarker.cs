using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace FluentDocker.Tests.CoreTests.Driver
{
  /// <summary>
  /// Shared synchronization for the fake podman/docker CLI script tests. Those fakes write a
  /// marker file as their first shell line to signal "the child is running", but under a fully
  /// parallel + coverage-instrumented unit run the child's very first instruction can be delayed
  /// several seconds purely by process spawn / OS scheduling latency (measured p99 ~3.6 s on a
  /// 14-core box under CPU saturation, worse under coverage). Every test must therefore wait on a
  /// generous WALL-CLOCK ceiling — not an iteration count, which thread-pool starvation stretches
  /// unpredictably. The ceiling is a cap, not a delay: the marker normally appears in tens of ms
  /// and the wait returns immediately. Centralised here so no test reintroduces a too-tight budget.
  /// </summary>
  internal static class FakeProcessMarker
  {
    // 30 s is ~8x the observed p99 spawn latency and leaves ample headroom for coverage overhead;
    // a genuine "child never started" hang still fails fast enough to be actionable in CI.
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    public static async Task WaitForFileAsync(string path, CancellationToken cancellationToken = default)
    {
      var deadline = DateTime.UtcNow + DefaultTimeout;
      while (!File.Exists(path))
      {
        if (DateTime.UtcNow > deadline)
          throw new TimeoutException($"Timed out waiting for {path}");
        await Task.Delay(20, cancellationToken).ConfigureAwait(false);
      }
    }
  }
}
