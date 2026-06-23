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
    private const string TestModel = "ai/smollm2";
    private const string EmbedModel = "ai/embeddinggemma";

    private FluentDockerKernel _kernel = null!;

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
    }

    public async ValueTask DisposeAsync()
    {
      if (_kernel != null)
        await _kernel.DisposeAsync();
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

      // load (detached). NOTE: --ignore-runtime-memory-check is a newer-DMR flag not
      // supported by every `docker model run`; we use a plain detached load here.
      await runner.LoadAsync(reference, new ModelRunOptions { Detach = true }, ct);

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

      // unload
      await runner.UnloadAsync(reference, false, ct);
    }

    [Fact]
    public async Task Chat_NonStreaming_ReturnsUsage()
    {
      var ct = TestContext.Current.CancellationToken;
      await using var runner = BuildRunner(TestModel);

      var response = await runner.ChatCompletionAsync(new ChatCompletionRequest
      {
        Model = TestModel,
        Messages = new List<ChatMessage> { new() { Role = "user", Content = "Say hello." } },
        MaxTokens = 16
      }, ct);

      Assert.NotEmpty(response.Choices);
      Assert.False(string.IsNullOrEmpty(response.Choices[0].Message.Content));
      Assert.True(response.Usage.TotalTokens > 0);
    }

    [Fact]
    public async Task Embeddings_RealModel()
    {
      var ct = TestContext.Current.CancellationToken;
      await using var runner = BuildRunner(EmbedModel, pullIfMissing: true);

      var vector = await runner.EmbedAsync("hello world", null, ct);
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
