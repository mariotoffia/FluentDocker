using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers.Docker.Cli;
using FluentDocker.Drivers.Docker.Cli.Components;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Models;
using FluentDocker.Model.Models.Options;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver
{
  [Trait("Category", "Unit")]
  public partial class DockerCliModelRuntimeDriverTests
  {
    [Fact]
    public async Task ConfigureAsync_ContextSize()
    {
      var driver = new FakeRuntimeDriver { Responder = _ => Ok() };
      await driver.ConfigureAsync(Ctx, ModelReference.Parse("ai/x"),
          new ModelConfigureOptions { ContextSize = 8192 }, TestContext.Current.CancellationToken);

      Assert.Contains("--context-size 8192", driver.Commands.Single());
    }

    [Fact]
    public async Task ConfigureAsync_ResetContextSize_EmitsMinusOne()
    {
      var driver = new FakeRuntimeDriver { Responder = _ => Ok() };
      await driver.ConfigureAsync(Ctx, ModelReference.Parse("ai/x"),
          new ModelConfigureOptions { ResetContextSize = true }, TestContext.Current.CancellationToken);

      Assert.Contains("--context-size -1", driver.Commands.Single());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("auto")]
    [InlineData("AUTO")]
    public async Task ConfigureAsync_AutoBackend_EmitsNoBackendFlag_AndDoesNotProbe(string? backend)
    {
      // The default ("auto" / unset) lets DMR pick the engine from the model format —
      // no `--backend` is emitted and the capability `--help` probe is never spawned.
      var driver = new FakeRuntimeDriver { Responder = _ => Ok() };
      await driver.ConfigureAsync(Ctx, ModelReference.Parse("ai/x"),
          new ModelConfigureOptions { ContextSize = 4096, Backend = backend! }, TestContext.Current.CancellationToken); // intentional null to verify null-handling

      Assert.DoesNotContain("--backend", driver.Commands.Single());
      Assert.DoesNotContain(driver.Commands, c => c.Contains("--help"));
    }

    [Fact]
    public async Task ConfigureAsync_ExplicitBackend_Unsupported_FailsClearly()
    {
      // Installed `docker model configure --help` does NOT advertise `--backend`, so an
      // explicit backend must fail with a clear message — not emit a rejected flag.
      var driver = new FakeRuntimeDriver
      {
        Responder = args => args.Contains("--help")
            ? Ok("Options:\n  --context-size int32\n  --mode string\n  --think\n")
            : Ok()
      };

      var result = await driver.ConfigureAsync(Ctx, ModelReference.Parse("ai/x"),
          new ModelConfigureOptions { Backend = "vllm" }, TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Model.ConfigureFailed, result.ErrorCode);
      Assert.Contains("backend", result.Error, StringComparison.OrdinalIgnoreCase);
      // The unsupported flag was never sent to a real `configure` invocation.
      Assert.DoesNotContain(driver.Commands, c => c.Contains("--backend"));
    }

    [Fact]
    public async Task ConfigureAsync_ExplicitBackend_Supported_EmitsBackend()
    {
      // Forward-compatible: when a future `docker model configure` advertises `--backend`,
      // the explicit backend is emitted.
      var driver = new FakeRuntimeDriver
      {
        Responder = args => args.Contains("--help")
            ? Ok("Options:\n  --backend string   inference backend\n  --context-size int32\n")
            : Ok()
      };

      var result = await driver.ConfigureAsync(Ctx, ModelReference.Parse("ai/x"),
          new ModelConfigureOptions { Backend = "vllm" }, TestContext.Current.CancellationToken);

      Assert.True(result.Success);
      Assert.Contains(driver.Commands, c => c.Contains("--backend vllm") && !c.Contains("--help"));
    }

    [Fact]
    public async Task ConfigureAsync_FirstCallerCancelsToken_DoesNotPoisonSharedBackendProbe()
    {
      // Regression: the shared `--backend` capability probe used to be cached together
      // with the FIRST caller's CancellationToken. If that first caller cancelled, the
      // cached probe task ended Canceled and every later explicit-backend call inherited
      // the dead task — failing forever. The probe must run with CancellationToken.None.
      var driver = new FakeRuntimeDriver
      {
        Responder = args => args.Contains("--help")
            ? Ok("Options:\n  --backend string   inference backend\n  --context-size int32\n")
            : Ok()
      };

      using var cancelled = new CancellationTokenSource();
      cancelled.Cancel();

      // First caller arrives with an already-cancelled token. With the old code this would
      // bind the probe to that token and poison the cache; now the probe ignores it.
      await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
          driver.ConfigureAsync(Ctx, ModelReference.Parse("ai/x"),
              new ModelConfigureOptions { Backend = "vllm" }, cancelled.Token));

      // A SECOND, healthy caller must still succeed (re-probe or reuse a non-cancelled task).
      var result = await driver.ConfigureAsync(Ctx, ModelReference.Parse("ai/y"),
          new ModelConfigureOptions { Backend = "vllm" }, TestContext.Current.CancellationToken);

      Assert.True(result.Success);
      Assert.Contains(driver.Commands, c => c.Contains("--backend vllm") && !c.Contains("--help"));
    }

    [Fact]
    public async Task ConfigureAsync_CancelledProbe_IsEvictedAndReProbed()
    {
      // A probe that ends Canceled must be evicted from the cache, not reused: the next
      // explicit-backend call re-probes instead of inheriting the dead (cancelled) task.
      // Here the first `--help` invocation throws OperationCanceledException, the second
      // returns valid help advertising `--backend`.
      var helpCalls = 0;
      var driver = new FakeRuntimeDriver
      {
        Responder = args =>
        {
          if (!args.Contains("--help"))
            return Ok();

          return ++helpCalls == 1
              ? throw new OperationCanceledException("probe cancelled")
              : Ok("Options:\n  --backend string   inference backend\n");
        }
      };

      // First call: the probe is cancelled, so awaiting it rethrows OperationCanceledException.
      await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
          driver.ConfigureAsync(Ctx, ModelReference.Parse("ai/x"),
              new ModelConfigureOptions { Backend = "vllm" }, TestContext.Current.CancellationToken));

      // Second call: the cached cancelled task was evicted, so it re-probes and now succeeds.
      var second = await driver.ConfigureAsync(Ctx, ModelReference.Parse("ai/y"),
          new ModelConfigureOptions { Backend = "vllm" }, TestContext.Current.CancellationToken);
      Assert.True(second.Success);
      Assert.Contains(driver.Commands, c => c.Contains("--backend vllm") && !c.Contains("--help"));
      Assert.Equal(2, helpCalls);
    }

    [Fact]
    public async Task ConfigureAsync_ExplicitBackend_SupportedProbeCachedAcrossCalls()
    {
      // The successful probe IS cached: a second explicit-backend call must not re-spawn
      // the `--help` capability probe.
      var helpCalls = 0;
      var driver = new FakeRuntimeDriver
      {
        Responder = args =>
        {
          if (!args.Contains("--help"))
            return Ok();

          helpCalls++;
          return Ok("Options:\n  --backend string   inference backend\n");
        }
      };

      var first = await driver.ConfigureAsync(Ctx, ModelReference.Parse("ai/x"),
          new ModelConfigureOptions { Backend = "vllm" }, TestContext.Current.CancellationToken);
      var second = await driver.ConfigureAsync(Ctx, ModelReference.Parse("ai/y"),
          new ModelConfigureOptions { Backend = "vllm" }, TestContext.Current.CancellationToken);

      Assert.True(first.Success);
      Assert.True(second.Success);
      Assert.Equal(1, helpCalls);
    }

    [Fact]
    public async Task ConfigureAsync_HfOverrides()
    {
      var driver = new FakeRuntimeDriver { Responder = _ => Ok() };
      await driver.ConfigureAsync(Ctx, ModelReference.Parse("ai/x"),
          new ModelConfigureOptions { HfOverridesJson = "{\"max_model_len\":8192}" }, TestContext.Current.CancellationToken);

      Assert.Contains("--hf_overrides", driver.Commands.Single());
      Assert.Contains("max_model_len", driver.Commands.Single());
    }

    [Fact]
    public async Task ConfigureAsync_RuntimeFlags_AfterDoubleDash_AndRefBefore()
    {
      var driver = new FakeRuntimeDriver { Responder = _ => Ok() };
      await driver.ConfigureAsync(Ctx, ModelReference.Parse("ai/x"),
          new ModelConfigureOptions { RuntimeFlags = new[] { "--temp", "0.7", "--top-p", "0.9" } }, TestContext.Current.CancellationToken);

      var cmd = driver.Commands.Single();
      var dashIndex = cmd.IndexOf(" -- ", StringComparison.Ordinal);
      var refIndex = cmd.IndexOf("ai/x", StringComparison.Ordinal);
      Assert.True(dashIndex > 0, "expected a ' -- ' separator");
      Assert.True(refIndex >= 0 && refIndex < dashIndex, "model ref must precede the -- separator");
      Assert.Contains("-- --temp 0.7 --top-p 0.9", cmd);
    }

    // ---- H8: the backend-capability probe honors the caller token + has a timeout ----

    /// <summary>
    /// A runtime driver whose <c>--help</c> probe BLOCKS on the token it is given (i.e. the
    /// probe's own internal-timeout token) until that token cancels, with a tiny probe
    /// timeout so the test does not wait the production 5s. Non-help commands respond OK.
    /// </summary>
    private sealed class BlockingProbeRuntimeDriver : DockerCliModelRuntimeDriver
    {
      private readonly TimeSpan _probeTimeout;
      public int HelpCalls;

      public BlockingProbeRuntimeDriver(TimeSpan probeTimeout) : base(null!) => _probeTimeout = probeTimeout;

      protected override TimeSpan BackendProbeTimeout => _probeTimeout;

      protected override async Task<SimpleCommandResult> RunAsync(DriverContext context, string arguments, CancellationToken cancellationToken)
      {
        if (!arguments.Contains("--help"))
          return new SimpleCommandResult { Success = true, Output = string.Empty, ExitCode = 0 };

        Interlocked.Increment(ref HelpCalls);
        // Block until the probe's OWN token (internal timeout) cancels — never returns on its
        // own. This proves the caller cannot affect this task and the internal timeout works.
        var tcs = new TaskCompletionSource();
        using (cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken)))
          await tcs.Task.ConfigureAwait(false);

        return new SimpleCommandResult { Success = true, Output = string.Empty, ExitCode = 0 };
      }
    }

    [Fact]
    public async Task ConfigureAsync_ProbeBlocks_CallerCancellationSurfacesOce_WithoutWaitingProbe()
    {
      // The probe BLOCKS (long internal timeout). An already-cancelled CALLER token must abort
      // the caller's wait immediately with OperationCanceledException — proving the await
      // honors the caller token (WaitAsync) instead of waiting for the shared probe.
      var driver = new BlockingProbeRuntimeDriver(TimeSpan.FromSeconds(30));
      using var cancelled = new CancellationTokenSource();
      cancelled.Cancel();

      await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
          driver.ConfigureAsync(Ctx, ModelReference.Parse("ai/x"),
              new ModelConfigureOptions { Backend = "vllm" }, cancelled.Token));
    }

    [Fact]
    public async Task ConfigureAsync_ProbeTimesOut_FailsAndIsEvictedSoLaterCallReProbes()
    {
      // A wedged probe must not hang forever and must not be cached permanently: after the
      // short internal timeout fires the explicit-backend ConfigureAsync surfaces a failure,
      // and the dead (cancelled) probe is evicted so a subsequent call re-probes.
      var driver = new BlockingProbeRuntimeDriver(TimeSpan.FromMilliseconds(150));

      // First call: probe blocks, internal timeout fires → a clear failure (OCE), not a hang.
      await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
          driver.ConfigureAsync(Ctx, ModelReference.Parse("ai/x"),
              new ModelConfigureOptions { Backend = "vllm" }, TestContext.Current.CancellationToken));

      // Second call: the timed-out probe was evicted, so a fresh probe is spawned (re-probe).
      await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
          driver.ConfigureAsync(Ctx, ModelReference.Parse("ai/y"),
              new ModelConfigureOptions { Backend = "vllm" }, TestContext.Current.CancellationToken));

      Assert.Equal(2, driver.HelpCalls);
    }
  }
}
