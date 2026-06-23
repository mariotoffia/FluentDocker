using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Models;
using FluentDocker.Model.Models.Inference;
using FluentDocker.Model.Models.Options;
using Moq;

namespace FluentDocker.Tests.Mocks
{
  /// <summary>
  /// Docker Model Runner (DMR) mock-driver helpers for <see cref="MockDriverPack"/>.
  /// Model ports are opt-in (mirroring the Swarm / PodmanKube pattern): call
  /// <see cref="EnableModelDrivers"/> before kernel registration.
  /// </summary>
  public partial class MockDriverPack
  {
    /// <summary>The mock model-management driver.</summary>
    public Mock<IModelManagementDriver> ModelManagementDriver { get; } = new Mock<IModelManagementDriver>();

    /// <summary>The mock model-runtime driver.</summary>
    public Mock<IModelRuntimeDriver> ModelRuntimeDriver { get; } = new Mock<IModelRuntimeDriver>();

    /// <summary>The mock model-inference driver.</summary>
    public Mock<IModelInferenceDriver> ModelInferenceDriver { get; } = new Mock<IModelInferenceDriver>();

    /// <summary>Registers the three model ports so the kernel can resolve them.</summary>
    /// <returns>This pack for chaining.</returns>
    public MockDriverPack EnableModelDrivers()
    {
      _drivers[typeof(IModelManagementDriver)] = ModelManagementDriver.Object;
      _drivers[typeof(IModelRuntimeDriver)] = ModelRuntimeDriver.Object;
      _drivers[typeof(IModelInferenceDriver)] = ModelInferenceDriver.Object;
      return this;
    }

    /// <summary>Sets up <c>ListAsync</c> to return the given models.</summary>
    public MockDriverPack SetupModelList(params ModelInfo[] models)
    {
      ModelManagementDriver
          .Setup(d => d.ListAsync(It.IsAny<DriverContext>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<IList<ModelInfo>>.Ok([.. models]));
      return this;
    }

    /// <summary>Sets up <c>InspectAsync</c> to return the given model.</summary>
    public MockDriverPack SetupModelInspect(ModelInfo model)
    {
      ModelManagementDriver
          .Setup(d => d.InspectAsync(It.IsAny<DriverContext>(), It.IsAny<ModelReference>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<ModelInfo>.Ok(model));
      return this;
    }

    /// <summary>Sets up <c>PullAsync</c> to return the given model.</summary>
    public MockDriverPack SetupModelPull(ModelInfo model)
    {
      ModelManagementDriver
          .Setup(d => d.PullAsync(It.IsAny<DriverContext>(), It.IsAny<ModelReference>(),
              It.IsAny<System.IProgress<ModelPullProgress>>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<ModelInfo>.Ok(model));
      return this;
    }

    /// <summary>Sets up <c>RemoveAsync</c> to succeed.</summary>
    public MockDriverPack SetupModelRemove()
    {
      ModelManagementDriver
          .Setup(d => d.RemoveAsync(It.IsAny<DriverContext>(), It.IsAny<ModelReference>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));
      return this;
    }

    /// <summary>Sets up <c>StatusAsync</c> to report the runner state.</summary>
    public MockDriverPack SetupModelStatus(bool running, string backend = "llama.cpp")
    {
      ModelRuntimeDriver
          .Setup(d => d.StatusAsync(It.IsAny<DriverContext>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<ModelRunnerStatus>.Ok(new ModelRunnerStatus
          {
            Running = running,
            Backend = backend,
            Endpoint = new System.Uri("http://localhost:12434")
          }));
      return this;
    }

    /// <summary>Sets up <c>LoadAsync</c> to succeed.</summary>
    public MockDriverPack SetupModelLoad()
    {
      ModelRuntimeDriver
          .Setup(d => d.LoadAsync(It.IsAny<DriverContext>(), It.IsAny<ModelReference>(), It.IsAny<ModelRunOptions>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));
      return this;
    }

    /// <summary>Sets up <c>ConfigureAsync</c> to succeed.</summary>
    public MockDriverPack SetupModelConfigure()
    {
      ModelRuntimeDriver
          .Setup(d => d.ConfigureAsync(It.IsAny<DriverContext>(), It.IsAny<ModelReference>(), It.IsAny<ModelConfigureOptions>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));
      return this;
    }

    /// <summary>Sets up <c>UnloadAsync</c> to succeed.</summary>
    public MockDriverPack SetupModelUnload()
    {
      ModelRuntimeDriver
          .Setup(d => d.UnloadAsync(It.IsAny<DriverContext>(), It.IsAny<ModelReference>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));
      return this;
    }

    /// <summary>Sets up <c>ListRunningAsync</c> to return the given running models.</summary>
    public MockDriverPack SetupModelRunning(params RunningModel[] running)
    {
      ModelRuntimeDriver
          .Setup(d => d.ListRunningAsync(It.IsAny<DriverContext>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<IList<RunningModel>>.Ok([.. running]));
      return this;
    }

    /// <summary>Sets up <c>ChatCompletionAsync</c> to return a single assistant message.</summary>
    public MockDriverPack SetupModelChat(string content)
    {
      ModelInferenceDriver
          .Setup(d => d.ChatCompletionAsync(It.IsAny<DriverContext>(), It.IsAny<ChatCompletionRequest>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<ChatCompletionResponse>.Ok(new ChatCompletionResponse
          {
            Id = "chatcmpl-mock",
            Object = "chat.completion",
            Choices = new List<ChatChoice>
            {
              new() { Index = 0, FinishReason = "stop", Message = new ChatMessage { Role = "assistant", Content = content } }
            },
            Usage = new Usage { PromptTokens = 1, CompletionTokens = 1, TotalTokens = 2 }
          }));
      return this;
    }

    /// <summary>Sets up <c>ChatCompletionAsync</c> to fail with the given error.</summary>
    public MockDriverPack SetupModelChatFailure(string error, string errorCode)
    {
      ModelInferenceDriver
          .Setup(d => d.ChatCompletionAsync(It.IsAny<DriverContext>(), It.IsAny<ChatCompletionRequest>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<ChatCompletionResponse>.Fail(error, errorCode));
      return this;
    }

    /// <summary>Sets up <c>ChatCompletionStreamAsync</c> to yield a chunk per token.</summary>
    public MockDriverPack SetupModelChatStream(params string[] tokens)
    {
      ModelInferenceDriver
          .Setup(d => d.ChatCompletionStreamAsync(It.IsAny<DriverContext>(), It.IsAny<ChatCompletionRequest>(), It.IsAny<CancellationToken>()))
          .Returns(ChatChunks(tokens));
      return this;
    }

    /// <summary>Sets up <c>EmbeddingsAsync</c> to return a single embedding vector.</summary>
    public MockDriverPack SetupModelEmbeddings(params float[] vector)
    {
      ModelInferenceDriver
          .Setup(d => d.EmbeddingsAsync(It.IsAny<DriverContext>(), It.IsAny<EmbeddingsRequest>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<EmbeddingsResponse>.Ok(new EmbeddingsResponse
          {
            Object = "list",
            Model = "mock",
            Data = new List<EmbeddingData> { new() { Index = 0, Embedding = [.. vector] } }
          }));
      return this;
    }

    private static async IAsyncEnumerable<ChatCompletionChunk> ChatChunks(string[] tokens)
    {
      await Task.CompletedTask;
      foreach (var token in tokens)
      {
        yield return new ChatCompletionChunk
        {
          Id = "chatcmpl-mock",
          Object = "chat.completion.chunk",
          Choices = new List<ChatChunkChoice>
          {
            new() { Index = 0, Delta = new ChatMessage { Content = token } }
          }
        };
      }
    }
  }
}
