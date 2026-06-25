using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Models;
using FluentDocker.Model.Models.Inference;
using FluentDocker.Services.Impl;
using FluentDocker.Tests.Mocks;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Service
{
  /// <summary>
  /// Facade-coverage tests for the <c>IModelInference</c> methods plus the
  /// cross-cutting fail-fast (<c>RequireModelId</c>), disposed-guard
  /// (<c>ThrowIfDisposed</c>) and cancellation behaviors. Partial of
  /// <see cref="ModelRunnerServiceTests"/>.
  /// </summary>
  public partial class ModelRunnerServiceTests
  {
    /// <summary>
    /// Additive build helper that also surfaces the configured <see cref="MockDriverPack"/>
    /// so a test can <c>Verify</c> driver invocation. Does not change the existing
    /// <c>BuildAsync</c> helper.
    /// </summary>
    private static async Task<(FluentDocker.Kernel.FluentDockerKernel kernel, ModelRunnerService runner, MockDriverPack pack)>
        BuildWithPackAsync(Action<MockDriverPack> configure = null, bool enable = true, bool withDefaultModel = true)
    {
      var pack = new MockDriverPack();
      configure?.Invoke(pack);
      if (enable)
        pack.EnableModelDrivers();

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", pack);
      var runner = new ModelRunnerService(kernel, "docker", ModelRunnerEndpoint.HostTcp(),
          withDefaultModel ? ModelReference.Parse("ai/smollm2") : null);
      return (kernel, runner, pack);
    }

    // ======================== IModelInference =============================

    [Fact]
    public async Task Inference_ChatCompletionStreamAsync_ProjectsChunks()
    {
      var (kernel, runner) = await BuildAsync(p => p.SetupModelChatStream("Hel", "lo"));
      await using (kernel)
      {
        var chunks = new List<ChatCompletionChunk>();
        await foreach (var chunk in runner.ChatCompletionStreamAsync(
            new ChatCompletionRequest { Model = "ai/smollm2" }, TestContext.Current.CancellationToken))
          chunks.Add(chunk);

        Assert.Equal(2, chunks.Count);
        Assert.Equal("Hel", chunks[0].Choices[0].Delta.Content);
        Assert.Equal("lo", chunks[1].Choices[0].Delta.Content);
      }
    }

    [Fact]
    public async Task Inference_CompletionAsync_TranslatesData()
    {
      var (kernel, runner) = await BuildAsync(p => p.SetupModelCompletion("the answer"));
      await using (kernel)
      {
        var resp = await runner.CompletionAsync(
            new CompletionRequest { Model = "ai/smollm2", Prompt = "q" }, TestContext.Current.CancellationToken);
        Assert.Equal("the answer", resp.Choices[0].Text);
      }
    }

    [Fact]
    public async Task Inference_CompletionAsync_Failure_ThrowsModelRunnerException()
    {
      var (kernel, runner) = await BuildAsync(p =>
          p.ModelInferenceDriver.Setup(d => d.CompletionAsync(It.IsAny<DriverContext>(), It.IsAny<CompletionRequest>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(CommandResponse<CompletionResponse>.Fail("nope", ErrorCodes.ModelInference.RequestFailed)));
      await using (kernel)
      {
        var ex = await Assert.ThrowsAsync<ModelRunnerException>(
            () => runner.CompletionAsync(new CompletionRequest { Model = "ai/smollm2" }, TestContext.Current.CancellationToken));
        Assert.Equal(ErrorCodes.ModelInference.RequestFailed, ex.ErrorCode);
      }
    }

    [Fact]
    public async Task Inference_CompletionStreamAsync_ProjectsChunks()
    {
      var (kernel, runner) = await BuildAsync(p => p.SetupModelCompletionStream("a", "b", "c"));
      await using (kernel)
      {
        var texts = new List<string>();
        await foreach (var chunk in runner.CompletionStreamAsync(
            new CompletionRequest { Model = "ai/smollm2" }, TestContext.Current.CancellationToken))
          texts.Add(chunk.Choices[0].Text);

        Assert.Equal(new[] { "a", "b", "c" }, texts);
      }
    }

    [Fact]
    public async Task Inference_EmbeddingsAsync_TranslatesData()
    {
      var (kernel, runner) = await BuildAsync(p => p.SetupModelEmbeddings(0.5f, 0.25f));
      await using (kernel)
      {
        var resp = await runner.EmbeddingsAsync(
            new EmbeddingsRequest { Model = "ai/smollm2", Input = new List<string> { "hi" } },
            TestContext.Current.CancellationToken);
        Assert.Single(resp.Data);
        Assert.Equal(2, resp.Data[0].Embedding.Count);
        Assert.Equal(0.5f, resp.Data[0].Embedding[0], 3);
      }
    }

    [Fact]
    public async Task Inference_EmbeddingsAsync_Failure_ThrowsModelRunnerException()
    {
      var (kernel, runner) = await BuildAsync(p =>
          p.ModelInferenceDriver.Setup(d => d.EmbeddingsAsync(It.IsAny<DriverContext>(), It.IsAny<EmbeddingsRequest>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(CommandResponse<EmbeddingsResponse>.Fail("emb fail", ErrorCodes.ModelInference.RequestFailed)));
      await using (kernel)
      {
        var ex = await Assert.ThrowsAsync<ModelRunnerException>(
            () => runner.EmbeddingsAsync(new EmbeddingsRequest { Model = "ai/smollm2" }, TestContext.Current.CancellationToken));
        Assert.Equal(ErrorCodes.ModelInference.RequestFailed, ex.ErrorCode);
      }
    }

    [Fact]
    public async Task Inference_ListEngineModelsAsync_TranslatesData()
    {
      var (kernel, runner) = await BuildAsync(p => p.SetupModelEngineModels(
          new OpenAiModel { Id = "ai/smollm2", Object = "model", OwnedBy = "docker" }));
      await using (kernel)
      {
        var models = await runner.ListEngineModelsAsync(TestContext.Current.CancellationToken);
        Assert.Single(models);
        Assert.Equal("ai/smollm2", models[0].Id);
      }
    }

    [Fact]
    public async Task Inference_ListEngineModelsAsync_Failure_ThrowsModelRunnerException()
    {
      var (kernel, runner) = await BuildAsync(p =>
          p.ModelInferenceDriver.Setup(d => d.ListEngineModelsAsync(It.IsAny<DriverContext>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(CommandResponse<IList<OpenAiModel>>.Fail("list fail", ErrorCodes.ModelInference.RequestFailed)));
      await using (kernel)
      {
        var ex = await Assert.ThrowsAsync<ModelRunnerException>(() => runner.ListEngineModelsAsync(TestContext.Current.CancellationToken));
        Assert.Equal(ErrorCodes.ModelInference.RequestFailed, ex.ErrorCode);
      }
    }

    // ======================== Fail-fast (RequireModelId) ==================

    [Fact]
    public async Task ChatAsync_NoDefaultModel_ThrowsArgumentException()
    {
      var (kernel, runner, _) = await BuildWithPackAsync(p => p.SetupModelChat("hi"), withDefaultModel: false);
      await using (kernel)
      {
        await Assert.ThrowsAsync<ArgumentException>(() => runner.ChatAsync("hello", TestContext.Current.CancellationToken));
      }
    }

    [Fact]
    public async Task EmbedAsync_NoDefaultModelAndNoModelArg_ThrowsArgumentException()
    {
      var (kernel, runner, _) = await BuildWithPackAsync(p => p.SetupModelEmbeddings(0.1f), withDefaultModel: false);
      await using (kernel)
      {
        await Assert.ThrowsAsync<ArgumentException>(() => runner.EmbedAsync("hello", null, TestContext.Current.CancellationToken));
      }
    }

    // ======================== H1: verbatim inference id ===================

    [Fact]
    public async Task ChatAsync_DefaultBareModel_SendsInferenceIdWithoutLatest()
    {
      // The kernel-backed runner's default model "ai/smollm2" is a Docker ref that
      // serializes as "ai/smollm2:latest", but the inference body must NOT carry the
      // auto :latest (H1 regression).
      ChatCompletionRequest captured = null;
      var (kernel, runner, _) = await BuildWithPackAsync(p =>
      {
        p.SetupModelChat("hi");
        p.ModelInferenceDriver
            .Setup(d => d.ChatCompletionAsync(It.IsAny<DriverContext>(), It.IsAny<ChatCompletionRequest>(), It.IsAny<CancellationToken>()))
            .Callback<DriverContext, ChatCompletionRequest, CancellationToken>((_, r, _) => captured = r)
            .ReturnsAsync(CommandResponse<ChatCompletionResponse>.Ok(new ChatCompletionResponse
            {
              Choices = new List<ChatChoice> { new() { Message = new ChatMessage { Role = "assistant", Content = "hi" } } }
            }));
      });
      await using (kernel)
      {
        await runner.ChatAsync("hello", TestContext.Current.CancellationToken);
        Assert.NotNull(captured);
        Assert.Equal("ai/smollm2", captured.Model);
        Assert.Equal("ai/smollm2:latest", runner.DefaultModel.ToString());
      }
    }

    [Fact]
    public async Task EmbedAsync_PerCallBareModel_SendsInferenceIdWithoutLatest()
    {
      EmbeddingsRequest captured = null;
      var (kernel, runner, _) = await BuildWithPackAsync(p =>
          p.ModelInferenceDriver
              .Setup(d => d.EmbeddingsAsync(It.IsAny<DriverContext>(), It.IsAny<EmbeddingsRequest>(), It.IsAny<CancellationToken>()))
              .Callback<DriverContext, EmbeddingsRequest, CancellationToken>((_, r, _) => captured = r)
              .ReturnsAsync(CommandResponse<EmbeddingsResponse>.Ok(new EmbeddingsResponse
              {
                Data = new List<EmbeddingData> { new() { Index = 0, Embedding = new List<float> { 0.1f } } }
              })),
          withDefaultModel: false);
      await using (kernel)
      {
        await runner.EmbedAsync("hi", ModelReference.Parse("ai/embeddinggemma"), TestContext.Current.CancellationToken);
        Assert.NotNull(captured);
        Assert.Equal("ai/embeddinggemma", captured.Model);
      }
    }

    // ======================== Disposed guard ==============================

    [Fact]
    public async Task Disposed_ListAsync_ThrowsObjectDisposedException()
    {
      var (kernel, runner) = await BuildAsync(p => p.SetupModelList(
          new ModelInfo { Reference = ModelReference.Parse("ai/smollm2") }));
      await using (kernel)
      {
        await runner.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => runner.ListAsync(TestContext.Current.CancellationToken));
      }
    }

    [Fact]
    public async Task Disposed_StatusAsync_ThrowsObjectDisposedException()
    {
      var (kernel, runner) = await BuildAsync(p => p.SetupModelStatus(running: true));
      await using (kernel)
      {
        await runner.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => runner.StatusAsync(TestContext.Current.CancellationToken));
      }
    }

    // ======================== Cancellation ================================

    [Fact]
    public async Task ListAsync_PreCancelledToken_SurfacesOperationCanceled()
    {
      var (kernel, runner) = await BuildAsync(p =>
          p.ModelManagementDriver.Setup(d => d.ListAsync(It.IsAny<DriverContext>(), It.IsAny<CancellationToken>()))
              .Returns<DriverContext, CancellationToken>((_, ct) =>
              {
                ct.ThrowIfCancellationRequested();
                return Task.FromResult(CommandResponse<IList<ModelInfo>>.Ok([]));
              }));
      await using (kernel)
      {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.ListAsync(cts.Token));
      }
    }

    [Fact]
    public async Task LogsAsync_PreCancelledToken_SurfacesOperationCanceledOnEnumeration()
    {
      var (kernel, runner) = await BuildAsync(p =>
          p.ModelRuntimeDriver.Setup(d => d.LogsAsync(It.IsAny<DriverContext>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
              .Returns<DriverContext, bool, CancellationToken>((_, _, ct) => CancelledLogStream(ct)));
      await using (kernel)
      {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
          await foreach (var _ in runner.LogsAsync(follow: true, cts.Token))
          {
            // The throw is lazy: it surfaces on first enumeration of the stream.
          }
        });
      }
    }

    private static async IAsyncEnumerable<string> CancelledLogStream(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
      await Task.CompletedTask;
      cancellationToken.ThrowIfCancellationRequested();
      yield return "unreachable";
    }
  }
}
