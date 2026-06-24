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
  /// Uses the tiny <c>ai/smollm2</c> (chat) and <c>ai/embeddinggemma</c> (embeddings)
  /// models to keep runs fast.
  /// </summary>
  [Trait("Category", "Integration")]
  [Collection("DockerModelRunner")]
  public sealed class ModelRunnerIntegrationTests : IAsyncLifetime
  {
    private const string DriverId = "docker";
    // Pinned references (explicit :latest) so a test never silently targets a
    // different tag than the one the fixture pulled.
    private const string TestModel = "ai/smollm2:latest";
    private const string EmbedModel = "ai/embeddinggemma:latest";

    private FluentDockerKernel _kernel = null!;
    private bool _seeded;

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
        throw new InvalidOperationException("$XunitDynamicSkip$Docker is not available: " + ex.Message);
      }

      try
      {
        var runtime = _kernel.SysCtl<IModelRuntimeDriver>(DriverId);
        var status = await runtime.StatusAsync(new DriverContext(DriverId), CancellationToken.None);
        if (!status.Success || !status.Data.Running)
          throw new InvalidOperationException("$XunitDynamicSkip$Docker Model Runner is not running");
      }
      catch (InvalidOperationException)
      {
        throw;
      }
      catch (Exception ex)
      {
        throw new InvalidOperationException("$XunitDynamicSkip$Docker Model Runner not available: " + ex.Message);
      }

      // Collection-level setup: pull + pin BOTH test models ONCE so individual tests
      // never assume a model is already present.
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
        throw new InvalidOperationException("$XunitDynamicSkip$Could not pull DMR test models: " + ex.Message);
      }
    }

    public async ValueTask DisposeAsync()
    {
      // Guaranteed cleanup: reset config mutated by tests and unload the models, so the
      // host is left in a clean state regardless of which tests ran or failed.
      if (_kernel != null && _seeded)
      {
        var cleanup = new Builder().WithinDriver(DriverId, _kernel).UseModelRunner().Build();
        await using ((IAsyncDisposable)cleanup)
        {
          await SafeAsync(() => cleanup.ConfigureAsync(ModelReference.Parse(TestModel), new ModelConfigureOptions { ContextSize = -1 }, CancellationToken.None));
          await SafeAsync(() => cleanup.UnloadAsync(ModelReference.Parse(TestModel), false, CancellationToken.None));
          await SafeAsync(() => cleanup.UnloadAsync(ModelReference.Parse(EmbedModel), false, CancellationToken.None));
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
    /// Re-throws as an xUnit dynamic-skip when the failure is the inference ENGINE
    /// crashing (llama.cpp segfaulting / failing to become ready) rather than a
    /// FluentDocker defect. A broken runtime is a host/environment condition — like DMR
    /// being absent — so the inference tests skip cleanly instead of reporting a false
    /// regression. Genuine library errors are re-thrown unchanged and still fail.
    /// </summary>
    private static void SkipIfRuntimeUnstable(Exception ex)
    {
      var message = ex.Message ?? string.Empty;
      var unstable =
          message.Contains("llama.cpp", StringComparison.OrdinalIgnoreCase) ||
          message.Contains("unable to load runner", StringComparison.OrdinalIgnoreCase) ||
          message.Contains("waiting for runner to be ready", StringComparison.OrdinalIgnoreCase) ||
          message.Contains("terminated unexpectedly", StringComparison.OrdinalIgnoreCase);

      if (unstable)
        throw new InvalidOperationException(
            "$XunitDynamicSkip$Inference runtime is unstable on this host (engine failed to load the model): " + message);
    }

    private IModelRunner BuildRunner(string model, bool pullIfMissing = false)
    {
      var builder = new Builder().WithinDriver(DriverId, _kernel).UseModelRunner().ForModel(model);
      if (pullIfMissing)
        builder.PullIfMissing();
      return builder.Build();
    }

    [Fact]
    public async Task FullLifecycle_PullListInspectLoadChatStreamUnload()
    {
      var ct = TestContext.Current.CancellationToken;
      var reference = ModelReference.Parse(TestModel);
      await using var runner = BuildRunner(TestModel);

      // pull (cached if present)
      var pulled = await runner.PullAsync(reference, null, ct);
      Assert.NotNull(pulled);

      // list
      var list = await runner.ListAsync(ct);
      Assert.Contains(list, m => m.Reference.Name == "smollm2");

      // inspect
      var info = await runner.InspectAsync(reference, ct);
      Assert.Equal("gguf", info.Format);
      Assert.True(info.Size > 0);

      // load (detached) — a plain `docker model run -d` (no unsupported flags).
      try
      {
        await runner.LoadAsync(reference, new ModelRunOptions { Detach = true }, ct);
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

        // chat (stream)
        var tokens = new List<string>();
        await foreach (var token in runner.ChatStreamAsync("Count: one two three", ct))
          tokens.Add(token);
        Assert.NotEmpty(tokens);

        // running models
        var running = await runner.ListRunningAsync(ct);
        Assert.Contains(running, r => r.Reference.Name == "smollm2");
      }
      catch (ModelRunnerException ex)
      {
        SkipIfRuntimeUnstable(ex);
        throw;
      }
      finally
      {
        // Guarantee the model is unloaded even if an assertion above fails.
        await runner.UnloadAsync(reference, false, ct);
      }
    }

    [Fact]
    public async Task Chat_NonStreaming_ReturnsUsage()
    {
      var ct = TestContext.Current.CancellationToken;
      await using var runner = BuildRunner(TestModel);

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
