using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers.Docker.Cli;
using FluentDocker.Drivers.Docker.Cli.Components;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Models;
using FluentDocker.Model.Models.Options;
using FluentDocker.Tests.Mocks;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver
{
  /// <summary>
  /// Unit tests for <see cref="DockerCliModelRuntimeDriver"/>, with emphasis on
  /// the <c>configure</c> command assembly (context-size / reset / backend /
  /// <c>--hf_overrides</c> / <c>--</c> runtime-flag passthrough).
  /// </summary>
  [Trait("Category", "Unit")]
  public class DockerCliModelRuntimeDriverTests
  {
    private static DriverContext Ctx => new("docker");

    private sealed class FakeRuntimeDriver : DockerCliModelRuntimeDriver
    {
      public List<string> Commands { get; } = [];
      public Func<string, SimpleCommandResult> Responder { get; set; }
      public Func<string, IEnumerable<string>> StreamResponder { get; set; }

      public FakeRuntimeDriver() : base(null)
      {
      }

      protected override Task<SimpleCommandResult> RunAsync(string arguments, CancellationToken cancellationToken)
      {
        Commands.Add(arguments);
        // Honor the token the way a real CLI invocation would: a caller's cancellation
        // surfaces as OperationCanceledException. This lets tests prove the shared
        // `--backend` probe is not bound to (and poisoned by) a single caller's token.
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Responder?.Invoke(arguments) ?? new SimpleCommandResult { Success = true, Output = string.Empty, ExitCode = 0 });
      }

      protected override IAsyncEnumerable<string> RunStreamingAsync(string arguments, CancellationToken cancellationToken)
      {
        Commands.Add(arguments);
        return ToAsync(StreamResponder?.Invoke(arguments) ?? Array.Empty<string>());
      }

      private static async IAsyncEnumerable<string> ToAsync(IEnumerable<string> items)
      {
        await Task.CompletedTask;
        foreach (var item in items)
          yield return item;
      }
    }

    private static SimpleCommandResult Ok(string output = "") => new() { Success = true, Output = output, ExitCode = 0 };

    [Fact]
    public async Task StatusAsync_Running()
    {
      var driver = new FakeRuntimeDriver { Responder = _ => Ok("Docker Model Runner is running\n") };
      var result = await driver.StatusAsync(Ctx, TestContext.Current.CancellationToken);

      Assert.True(result.Success);
      Assert.True(result.Data.Running);
      Assert.Contains("model status", driver.Commands.Single());
    }

    [Fact]
    public async Task StatusAsync_NotRunning()
    {
      var driver = new FakeRuntimeDriver { Responder = _ => Ok("Docker Model Runner is not running\n") };
      var result = await driver.StatusAsync(Ctx, TestContext.Current.CancellationToken);

      Assert.True(result.Success);
      Assert.False(result.Data.Running);
    }

    [Fact]
    public async Task StatusAsync_NonZeroExit_NotRunningShape_IsTreatedAsNotRunning()
    {
      // A non-zero exit whose output is the recognizable "not running" shape is a
      // legitimate state, not a command failure.
      var driver = new FakeRuntimeDriver
      {
        Responder = _ => new SimpleCommandResult { Success = false, ExitCode = 1, Output = "Docker Model Runner is not running\n" }
      };
      var result = await driver.StatusAsync(Ctx, TestContext.Current.CancellationToken);

      Assert.True(result.Success);
      Assert.False(result.Data.Running);
    }

    [Fact]
    public async Task StatusAsync_NonZeroExit_UnrelatedNotRunningMessage_Fails()
    {
      // A genuine command error whose text merely *contains* "not running" (e.g. an
      // unrelated daemon error about a container) must NOT be mistaken for the runner's
      // clean stopped state — only the specific "Model Runner is not running" phrase is.
      var driver = new FakeRuntimeDriver
      {
        Responder = _ => new SimpleCommandResult
        {
          Success = false,
          ExitCode = 1,
          Error = "Error response from daemon: container abc is not running"
        }
      };
      var result = await driver.StatusAsync(Ctx, TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Model.StatusFailed, result.ErrorCode);
    }

    [Fact]
    public async Task StatusAsync_NonZeroExit_UnrecognizedOutput_Fails()
    {
      // A non-zero exit that is NOT a recognizable status (e.g. docker missing) must
      // surface as a failure rather than silently reporting Running=false.
      var driver = new FakeRuntimeDriver
      {
        Responder = _ => new SimpleCommandResult { Success = false, ExitCode = 127, Error = "docker: command not found", Output = string.Empty }
      };
      var result = await driver.StatusAsync(Ctx, TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.Model.StatusFailed, result.ErrorCode);
      Assert.Equal(127, result.ExitCode);
    }

    [Fact]
    public async Task VersionAsync_Parses()
    {
      var driver = new FakeRuntimeDriver { Responder = _ => Ok("Client:\n Version:    v1.2.1\n") };
      var result = await driver.VersionAsync(Ctx, TestContext.Current.CancellationToken);

      Assert.True(result.Success);
      Assert.Equal("v1.2.1", result.Data.CliVersion);
    }

    [Fact]
    public async Task ListRunningAsync_ParsesPsTable()
    {
      var driver = new FakeRuntimeDriver { Responder = _ => Ok(DmrFixtures.Load("ps.txt")) };
      var result = await driver.ListRunningAsync(Ctx, TestContext.Current.CancellationToken);

      Assert.True(result.Success);
      Assert.Contains(result.Data, r => r.Reference.Name == "smollm2");
      Assert.Contains("model ps", driver.Commands.Single());
    }

    [Fact]
    public async Task LoadAsync_DetachedWithDebug_NeverEmitsUnsupportedFlags()
    {
      var driver = new FakeRuntimeDriver { Responder = _ => Ok() };
      await driver.LoadAsync(Ctx, ModelReference.Parse("ai/smollm2"),
          new ModelRunOptions { Detach = true, Debug = true }, TestContext.Current.CancellationToken);

      var cmd = driver.Commands.Single();
      Assert.Contains("model run", cmd);
      Assert.Contains("-d", cmd);
      Assert.Contains("--debug", cmd);
      Assert.Contains("ai/smollm2", cmd);
      // `docker model run` v1.2.1 exposes neither of these — emitting them fails the command.
      Assert.DoesNotContain("--ignore-runtime-memory-check", cmd);
      Assert.DoesNotContain("--backend", cmd);
    }

    [Fact]
    public async Task UnloadAsync_All()
    {
      var driver = new FakeRuntimeDriver { Responder = _ => Ok() };
      await driver.UnloadAsync(Ctx, ModelReference.Parse("ai/smollm2"), all: true, TestContext.Current.CancellationToken);
      Assert.Contains("--all", driver.Commands.Single());
    }

    [Fact]
    public async Task UnloadAsync_Single()
    {
      var driver = new FakeRuntimeDriver { Responder = _ => Ok() };
      await driver.UnloadAsync(Ctx, ModelReference.Parse("ai/smollm2"), all: false, TestContext.Current.CancellationToken);
      var cmd = driver.Commands.Single();
      Assert.Contains("model unload", cmd);
      Assert.Contains("ai/smollm2", cmd);
      Assert.DoesNotContain("--all", cmd);
    }

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
          new ModelConfigureOptions { ContextSize = 4096, Backend = backend }, TestContext.Current.CancellationToken);

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

    [Fact]
    public async Task LogsAsync_StreamsLines_FollowFlag()
    {
      var driver = new FakeRuntimeDriver { StreamResponder = _ => new[] { "line1", "line2" } };

      var collected = new List<string>();
      await foreach (var line in driver.LogsAsync(Ctx, follow: true, TestContext.Current.CancellationToken))
        collected.Add(line);

      Assert.Equal(new[] { "line1", "line2" }, collected);
      Assert.Contains("model logs", driver.Commands.Single());
      Assert.Contains("-f", driver.Commands.Single());
    }

    [Fact]
    public async Task InstallRunnerAsync_Gpu()
    {
      var driver = new FakeRuntimeDriver { Responder = _ => Ok() };
      await driver.InstallRunnerAsync(Ctx, new ModelRunnerInstallOptions { Gpu = "auto" }, TestContext.Current.CancellationToken);

      var cmd = driver.Commands.Single();
      Assert.Contains("model install-runner", cmd);
      Assert.Contains("--gpu auto", cmd);
    }

    [Fact]
    public async Task UninstallRunnerAsync_ImagesAndModels()
    {
      var driver = new FakeRuntimeDriver { Responder = _ => Ok() };
      await driver.UninstallRunnerAsync(Ctx, new ModelRunnerUninstallOptions { RemoveImages = true, RemoveModels = true }, TestContext.Current.CancellationToken);

      var cmd = driver.Commands.Single();
      Assert.Contains("model uninstall-runner", cmd);
      Assert.Contains("--images", cmd);
      Assert.Contains("--models", cmd);
    }
  }
}
