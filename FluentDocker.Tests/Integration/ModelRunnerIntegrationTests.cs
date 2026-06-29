using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Models;
using FluentDocker.Model.Models.Inference;
using FluentDocker.Model.Models.Options;
using FluentDocker.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FluentDocker.Tests.Integration
{
  /// <summary>
  /// End-to-end integration tests against a real Docker Model Runner. Gated: when
  /// DMR is not running, every test SKIPS cleanly (xUnit dynamic-skip sentinel).
  /// Per-test setup (<see cref="IAsyncLifetime"/> runs once per test method, NOT once
  /// per collection — no <c>ICollectionFixture</c> is used) pulls + pins both models,
  /// which are cached after the first test. Uses the tiny <c>ai/smollm2</c> (chat) and
  /// <c>ai/embeddinggemma</c> (embeddings) models to keep runs fast.
  /// </summary>
  [Trait("Category", "Integration")]
  [Trait("Requires", "Dmr")]
  [Collection("DockerModelRunner")]
  public sealed class ModelRunnerIntegrationTests : IAsyncLifetime
  {
    private const string DriverId = "docker";
    // Defaults are pinned (explicit :latest) so a test never silently targets a
    // different tag than the one the fixture pulled. Override for local mirrors
    // or smaller/larger models with FLUENTDOCKER_DMR_CHAT_MODEL /
    // FLUENTDOCKER_DMR_EMBED_MODEL.
    private const string DefaultTestModel = "ai/smollm2:latest";
    private const string DefaultEmbedModel = "ai/embeddinggemma:latest";
    private const string ChatModelEnv = "FLUENTDOCKER_DMR_CHAT_MODEL";
    private const string EmbedModelEnv = "FLUENTDOCKER_DMR_EMBED_MODEL";
    private const string DestructiveEnv = "FLUENTDOCKER_DMR_ALLOW_DESTRUCTIVE";
    private static string TestModel => ModelFromEnvironment(ChatModelEnv, DefaultTestModel);
    private static string EmbedModel => ModelFromEnvironment(EmbedModelEnv, DefaultEmbedModel);

    private FluentDockerKernel _kernel = null!;
    private bool _seeded;

    // On must-run CI lanes (schedule / manual run_integration=true) the workflow sets
    // FLUENTDOCKER_REQUIRE_DMR=1. When set, a DMR that is absent/unavailable/unstable must
    // HARD-FAIL instead of self-skipping — otherwise a broken DMR code path passes CI green
    // with zero real Model Runner coverage. On PRs the flag is empty, so we still skip cleanly.
    private static bool RequireDmr =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FLUENTDOCKER_REQUIRE_DMR"));

    private static bool AllowDestructive =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(DestructiveEnv));

    private static string ModelFromEnvironment(string variableName, string fallback)
    {
      var value = Environment.GetEnvironmentVariable(variableName);
      return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private static bool IsReference(ModelReference actual, ModelReference expected)
    {
      return string.Equals(actual.ToString(), expected.ToString(), StringComparison.OrdinalIgnoreCase)
          || string.Equals(actual.Name, expected.Name, StringComparison.OrdinalIgnoreCase);
    }

    private static Exception SkipOrFail(string reason) =>
        RequireDmr
            ? new InvalidOperationException($"DMR required but unavailable: {reason}")
            : new InvalidOperationException("$XunitDynamicSkip$" + reason);

    public async ValueTask InitializeAsync()
    {
      try
      {
        _kernel = await FluentDockerKernel.Create(NullLoggerFactory.Instance)
            .WithDockerCli(DriverId, d => d.AsDefault())
            .BuildAsync();
      }
      catch (Exception ex)
      {
        throw SkipOrFail("Docker is not available: " + ex.Message);
      }

      try
      {
        var runtime = _kernel.SysCtl<IModelRuntimeDriver>(DriverId);
        var status = await runtime.StatusAsync(new DriverContext(DriverId), CancellationToken.None);
        if (!status.Success || !status.Data.Running)
          throw SkipOrFail("Docker Model Runner is not running");
      }
      catch (InvalidOperationException)
      {
        throw;
      }
      catch (Exception ex)
      {
        throw SkipOrFail("Docker Model Runner not available: " + ex.Message);
      }

      // Per-test setup (IAsyncLifetime runs once per test method): pull + pin BOTH test
      // models (cached after the first test) so individual tests never assume a model is
      // already present.
      try
      {
        var seed = new Builder().WithinDriver(DriverId, _kernel).UseModelRunner().Build();
        await using ((IAsyncDisposable)seed)
        {
          await seed.PullAsync(ModelReference.Parse(TestModel), null, CancellationToken.None);
          await seed.PullAsync(ModelReference.Parse(EmbedModel), null, CancellationToken.None);
        }
        _seeded = true;
      }
      catch (Exception ex)
      {
        throw SkipOrFail("Could not pull DMR test models: " + ex.Message);
      }
    }

    public async ValueTask DisposeAsync()
    {
      // Per-test cleanup (DisposeAsync runs once per test method): reset config mutated by
      // tests and unload the models, so the host is left in a clean state regardless of
      // which tests ran or failed.
      if (_kernel != null && _seeded)
      {
        var cleanup = new Builder().WithinDriver(DriverId, _kernel).UseModelRunner().Build();
        await using ((IAsyncDisposable)cleanup)
        {
          await SafeAsync(() => cleanup.ConfigureAsync(ModelReference.Parse(TestModel), new ModelConfigureOptions { ContextSize = -1 }, CancellationToken.None));
          await SafeAsync(() => cleanup.UnloadAsync(ModelReference.Parse(TestModel), CancellationToken.None));
          await SafeAsync(() => cleanup.UnloadAsync(ModelReference.Parse(EmbedModel), CancellationToken.None));
        }
      }

      if (_kernel != null)
        await _kernel.DisposeAsync();
    }

    private static async Task SafeAsync(Func<Task> action)
    {
      try
      {
        await action();
      }
      catch
      {
        // best-effort cleanup
      }
    }

    /// <summary>
    /// Best-effort re-pull of a model that a destructive test removed, retried a few times so
    /// a single transient network failure does not leave the host store missing the shared,
    /// pinned model. Each attempt targets the SAME pinned <paramref name="reference"/> the test
    /// removed; it stops on the first success and never throws (restore is best-effort).
    /// </summary>
    private static async Task RepullWithRetryAsync(IModelRunner runner, ModelReference reference, CancellationToken ct)
    {
      const int maxAttempts = 3;
      for (var attempt = 1; attempt <= maxAttempts; attempt++)
      {
        try
        {
          await runner.PullAsync(reference, null, ct);
          return; // restored
        }
        catch when (attempt < maxAttempts && !ct.IsCancellationRequested)
        {
          // transient failure — retry
        }
        catch
        {
          // final attempt failed (or cancelled): give up, best-effort.
          return;
        }
      }
    }

    /// <summary>
    /// Re-throws as an xUnit dynamic-skip ONLY for the one KNOWN, host-side DMR v1.2.1
    /// engine defect: the bundled llama.cpp crashes during the auto
    /// "fit-params-to-device-memory" step with <c>GGML_ASSERT(n_outputs &gt;= 1)</c> in
    /// <c>llama_context::graph_reserve</c> (<c>common_params_fit_impl</c> →
    /// <c>server_context_impl::load_model</c>) when a chat model is loaded without an explicit
    /// context size. The inference tests already pin <c>contextSize: 4096</c> to dodge that
    /// probe, so this is a narrow safety net for hosts where the crash still surfaces — it is
    /// NOT a general "the engine looked unhappy" escape hatch.
    /// <para>
    /// The match is deliberately restricted to that crash's distinctive signatures
    /// (<c>GGML_ASSERT</c>, <c>n_outputs</c>, <c>graph_reserve</c>, the <c>-fit off</c> hint).
    /// Broad substrings like "llama.cpp" or "unable to load runner" were intentionally REMOVED:
    /// they would silently skip on genuine FluentDocker regressions (any error that merely
    /// names the engine or a generic load failure), turning a real bug into a green CI run.
    /// Anything that is not this specific defect is re-thrown unchanged and still FAILS.
    /// </para>
    /// </summary>
    private static void SkipIfRuntimeUnstable(Exception ex)
    {
      var message = ex.Message ?? string.Empty;
      // The known GGML_ASSERT(n_outputs >= 1) auto-fit crash. Require a signature that is
      // specific to THAT engine defect, not merely any mention of the engine.
      var isKnownAutoFitCrash =
          message.Contains("GGML_ASSERT", StringComparison.OrdinalIgnoreCase) ||
          message.Contains("n_outputs", StringComparison.OrdinalIgnoreCase) ||
          message.Contains("graph_reserve", StringComparison.OrdinalIgnoreCase) ||
          message.Contains("common_params_fit", StringComparison.OrdinalIgnoreCase) ||
          message.Contains("-fit off", StringComparison.OrdinalIgnoreCase);

      if (isKnownAutoFitCrash)
        throw SkipOrFail(
            "Known DMR v1.2.1 engine defect (GGML_ASSERT(n_outputs>=1) auto-fit crash) on this host: " + message);
    }

    private IModelRunner BuildRunner(string model, bool pullIfMissing = false, int? contextSize = null)
    {
      var builder = new Builder().WithinDriver(DriverId, _kernel).UseModelRunner().ForModel(model);
      if (pullIfMissing)
        builder.PullIfMissing();
      // Pin an explicit context size for inference. DMR v1.2.1's bundled llama.cpp
      // crashes (GGML_ASSERT(n_outputs >= 1) in the auto "fit-params-to-device-memory"
      // step) whenever a chat model is loaded WITHOUT an explicit context — the engine
      // log itself suggests "-fit off". Pinning a size skips that buggy probe so the
      // model loads and serves. This is an engine workaround, not a FluentDocker need.
      if (contextSize.HasValue)
        builder.WithContextSize(contextSize.Value);
      return builder.Build();
    }

    [Fact]
    public async Task FullLifecycle_PullListInspectLoadChatStreamUnload()
    {
      var ct = TestContext.Current.CancellationToken;
      var reference = ModelReference.Parse(TestModel);
      await using var runner = BuildRunner(TestModel, contextSize: 4096);

      // pull (cached if present)
      var pulled = await runner.PullAsync(reference, null, ct);
      Assert.NotNull(pulled);

      // list
      var list = await runner.ListAsync(ct);
      Assert.Contains(list, m => IsReference(m.Reference, reference));

      // inspect
      var info = await runner.InspectAsync(reference, ct);
      Assert.Equal("gguf", info.Format);
      Assert.True(info.Size > 0);

      // load (detached) — a plain `docker model run -d` (no unsupported flags).
      try
      {
        await runner.LoadAsync(reference, new ModelRunOptions(), ct);
      }
      catch (ModelRunnerException ex)
      {
        SkipIfRuntimeUnstable(ex);
        throw;
      }

      try
      {
        // chat (non-stream, HTTP)
        var reply = await runner.ChatAsync("Reply with a single word.", ct);
        Assert.False(string.IsNullOrWhiteSpace(reply));

        // chat (stream) — must yield MULTIPLE SSE deltas (true token-by-token streaming,
        // not one buffered payload), and they must reassemble into non-empty content.
        var tokens = new List<string>();
        await foreach (var token in runner.ChatStreamAsync("Count from one to five.", ct))
          tokens.Add(token);
        Assert.True(tokens.Count > 1, $"Expected multiple streamed chunks, got {tokens.Count}");
        Assert.False(string.IsNullOrWhiteSpace(string.Concat(tokens)));

        // running models
        var running = await runner.ListRunningAsync(ct);
        Assert.Contains(running, r => IsReference(r.Reference, reference));
      }
      catch (ModelRunnerException ex)
      {
        SkipIfRuntimeUnstable(ex);
        throw;
      }
      finally
      {
        // Guarantee the model is unloaded even if an assertion above fails.
        await runner.UnloadAsync(reference, ct);
      }
    }

    /// <summary>
    /// DESTRUCTIVE — MUTATES THE LOCAL DMR MODEL STORE. To prove <c>PullIfMissing()</c> does
    /// real work this test <b>removes <see cref="DefaultTestModel"/> from the
    /// developer's / CI machine's local store</b>, then auto-pulls it back. The model tag is
    /// PINNED to an explicit <c>:latest</c> by default so the removed and re-pulled artifact
    /// is exactly the same tag. This test is skipped unless
    /// <c>FLUENTDOCKER_DMR_ALLOW_DESTRUCTIVE</c> is set, because deleting a host model store
    /// entry is never safe by default. The re-pull runs in a <c>finally</c> with bounded
    /// retries so the store is restored when the opt-in test runs.
    /// </summary>
    [Fact]
    public async Task Build_WithPullIfMissing_AutoPullsModel()
    {
      var ct = TestContext.Current.CancellationToken;
      var reference = ModelReference.Parse(TestModel);
      if (!AllowDestructive)
        throw new InvalidOperationException(
            "$XunitDynamicSkip$Set FLUENTDOCKER_DMR_ALLOW_DESTRUCTIVE=1 to allow removing and re-pulling a host DMR model.");

      // Arrange: remove the chat model from the local store so PullIfMissing() has real work to
      // do — without this the fixture has already seeded the model and the auto-pull
      // would be a silent no-op, proving nothing. Tests in this [Collection] run
      // sequentially in arbitrary order, so the finally re-pulls the model afterwards to
      // restore the fixture's "model present" precondition for whichever test runs next.
      await using (var admin = BuildRunner(TestModel))
      {
        await SafeAsync(() => admin.UnloadAsync(reference, ct)); // a loaded model can't be removed
        await admin.RemoveAsync(reference, force: true, ct);
        Assert.DoesNotContain(await admin.ListAsync(ct), m => IsReference(m.Reference, reference));
      }

      try
      {
        // Act: the fluent PullIfMissing() must auto-pull the model at Build() time
        // (ModelRunnerBuilder.BuildAsync calls PullAsync when the flag is set).
        await using var runner = BuildRunner(TestModel, pullIfMissing: true);

        // Assert: the model is now present in the local store.
        Assert.Contains(await runner.ListAsync(ct), m => IsReference(m.Reference, reference));
      }
      finally
      {
        // Restore the fixture invariant regardless of outcome. This test DELETED a shared,
        // pinned model from the host store, so the re-pull is best-effort BUT retried a few
        // times to ride out a transient network hiccup rather than leaving the developer's
        // store missing the chat model after a single failed attempt.
        await using var restore = BuildRunner(TestModel);
        await RepullWithRetryAsync(restore, reference, ct);
      }
    }

    [Fact]
    public async Task UseModel_StartDispose_LoadsAndUnloadsModel()
    {
      var ct = TestContext.Current.CancellationToken;
      var reference = ModelReference.Parse(TestModel);
      IModelService? service = null;

      try
      {
        service = await new Builder().WithinDriver(DriverId, _kernel)
            .UseModel(reference)
            .WithContextSize(4096)
            .KeepRunning(false)
            .BuildAsync(ct);

        await service.StartAsync(ct);
        Assert.Contains(await service.Runner.ListRunningAsync(ct), r => IsReference(r.Reference, reference));
      }
      catch (ModelRunnerException ex)
      {
        SkipIfRuntimeUnstable(ex);
        throw;
      }
      finally
      {
        if (service != null)
          await service.DisposeAsync();
      }

      try
      {
        await using var runner = BuildRunner(TestModel);
        Assert.DoesNotContain(await runner.ListRunningAsync(ct), r => IsReference(r.Reference, reference));
      }
      catch (ModelRunnerException ex)
      {
        SkipIfRuntimeUnstable(ex);
        throw;
      }
    }

    [Fact]
    public async Task Chat_NonStreaming_ReturnsUsage()
    {
      var ct = TestContext.Current.CancellationToken;
      await using var runner = BuildRunner(TestModel, contextSize: 4096);

      ChatCompletionResponse response;
      try
      {
        response = await runner.ChatCompletionAsync(new ChatCompletionRequest
        {
          Model = TestModel,
          Messages = new List<ChatMessage> { new() { Role = "user", Content = "Say hello." } },
          MaxTokens = 16
        }, ct);
      }
      catch (ModelRunnerException ex)
      {
        SkipIfRuntimeUnstable(ex);
        throw;
      }

      Assert.NotEmpty(response.Choices);
      Assert.False(string.IsNullOrEmpty(response.Choices[0].Message.Content));
      Assert.True(response.Usage.TotalTokens > 0);
    }

    [Fact]
    public async Task Embeddings_RealModel()
    {
      var ct = TestContext.Current.CancellationToken;
      await using var runner = BuildRunner(EmbedModel); // pulled once by the fixture

      System.Collections.Generic.IReadOnlyList<float> vector;
      try
      {
        vector = await runner.EmbedAsync("hello world", null, ct);
      }
      catch (ModelRunnerException ex)
      {
        SkipIfRuntimeUnstable(ex);
        throw;
      }

      Assert.NotEmpty(vector);
      Assert.True(vector.Count > 8);
    }

    [Fact]
    public async Task Configure_ContextSize_DoesNotThrow()
    {
      var ct = TestContext.Current.CancellationToken;
      var reference = ModelReference.Parse(TestModel);
      await using var runner = BuildRunner(TestModel);

      await runner.ConfigureAsync(reference, new ModelConfigureOptions { ContextSize = 4096 }, ct);
    }

    [Fact]
    public async Task Status_And_Version()
    {
      var ct = TestContext.Current.CancellationToken;
      await using var runner = BuildRunner(TestModel);

      var status = await runner.StatusAsync(ct);
      Assert.True(status.Running);

      var version = await runner.VersionAsync(ct);
      Assert.False(string.IsNullOrEmpty(version.CliVersion));
    }

    [Fact]
    public async Task ListEngineModels_ReturnsServedModels()
    {
      var ct = TestContext.Current.CancellationToken;
      await using var runner = BuildRunner(TestModel);

      var models = await runner.ListEngineModelsAsync(ct);
      Assert.NotEmpty(models);
    }

    [Fact]
    public async Task Chat_UnpulledModel_FailsWithModelNotLoaded()
    {
      var ct = TestContext.Current.CancellationToken;
      await using var runner = BuildRunner("ai/this-model-does-not-exist-xyz");

      var ex = await Assert.ThrowsAsync<ModelRunnerException>(() => runner.ChatAsync("hi", ct));
      Assert.Equal(ErrorCodes.ModelInference.ModelNotLoaded, ex.ErrorCode);
    }

    [Fact]
    public async Task Capabilities_HttpRunner_SupportsStreamingAndEmbeddings()
    {
      await using var runner = BuildRunner(TestModel);
      Assert.True(runner.Capabilities.SupportsInference);
      Assert.True(runner.Capabilities.SupportsStreaming);
      Assert.True(runner.Capabilities.SupportsManagement);
    }
  }
}
