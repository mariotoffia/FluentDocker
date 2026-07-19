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
  /// Additional (purely additive) DMR mock-driver helpers for facade-coverage tests.
  /// These complement <see cref="MockDriverPack"/>'s existing model helpers without
  /// changing any existing helper behavior. New helpers only.
  /// </summary>
  public partial class MockDriverPack
  {
    // ---- Management (IModelStore) ---------------------------------------

    /// <summary>Sets up <c>TagAsync</c> to succeed.</summary>
    public MockDriverPack SetupModelTag()
    {
      ModelManagementDriver
          .Setup(d => d.TagAsync(It.IsAny<DriverContext>(), It.IsAny<ModelReference>(), It.IsAny<ModelReference>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));
      return this;
    }

    /// <summary>Sets up <c>PushAsync</c> to succeed.</summary>
    public MockDriverPack SetupModelPush()
    {
      ModelManagementDriver
          .Setup(d => d.PushAsync(It.IsAny<DriverContext>(), It.IsAny<ModelReference>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));
      return this;
    }

    /// <summary>Sets up <c>PackageAsync</c> to return the given model.</summary>
    public MockDriverPack SetupModelPackage(ModelInfo model)
    {
      ModelManagementDriver
          .Setup(d => d.PackageAsync(It.IsAny<DriverContext>(), It.IsAny<ModelPackageRequest>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<ModelInfo>.Ok(model));
      return this;
    }

    /// <summary>Sets up <c>PurgeAllAsync</c> to return the given prune result.</summary>
    public MockDriverPack SetupModelPurgeAll(ModelPruneResult result)
    {
      ModelManagementDriver
          .Setup(d => d.PurgeAllAsync(It.IsAny<DriverContext>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<ModelPruneResult>.Ok(result));
      return this;
    }

    /// <summary>Sets up <c>DiskUsageAsync</c> to return the given disk usage.</summary>
    public MockDriverPack SetupModelDiskUsage(ModelDiskUsage usage)
    {
      ModelManagementDriver
          .Setup(d => d.DiskUsageAsync(It.IsAny<DriverContext>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<ModelDiskUsage>.Ok(usage));
      return this;
    }

    // ---- Runtime (IModelEngine) -----------------------------------------

    /// <summary>Sets up <c>VersionAsync</c> to return the given version.</summary>
    public MockDriverPack SetupModelVersion(ModelRunnerVersion version)
    {
      ModelRuntimeDriver
          .Setup(d => d.VersionAsync(It.IsAny<DriverContext>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<ModelRunnerVersion>.Ok(version));
      return this;
    }

    /// <summary>Sets up <c>InstallRunnerAsync</c> to succeed.</summary>
    public MockDriverPack SetupModelInstallRunner()
    {
      ModelRuntimeDriver
          .Setup(d => d.InstallRunnerAsync(It.IsAny<DriverContext>(), It.IsAny<ModelRunnerInstallOptions>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));
      return this;
    }

    /// <summary>Sets up <c>LogsAsync</c> to yield the given log lines.</summary>
    public MockDriverPack SetupModelLogs(params string[] lines)
    {
      ModelRuntimeDriver
          .Setup(d => d.LogsAsync(It.IsAny<DriverContext>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
          .Returns(LogLines(lines));
      return this;
    }

    // ---- Inference (IModelInference) ------------------------------------

    /// <summary>Sets up <c>CompletionAsync</c> to return the given text.</summary>
    public MockDriverPack SetupModelCompletion(string text)
    {
      ModelInferenceDriver
          .Setup(d => d.CompletionAsync(It.IsAny<DriverContext>(), It.IsAny<CompletionRequest>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<CompletionResponse>.Ok(new CompletionResponse
          {
            Id = "cmpl-mock",
            Object = "text_completion",
            Choices = new List<CompletionChoice> { new() { Index = 0, Text = text, FinishReason = "stop" } },
            Usage = new Usage { PromptTokens = 1, CompletionTokens = 1, TotalTokens = 2 }
          }));
      return this;
    }

    /// <summary>Sets up <c>CompletionStreamAsync</c> to yield a chunk per token.</summary>
    public MockDriverPack SetupModelCompletionStream(params string[] tokens)
    {
      ModelInferenceDriver
          .Setup(d => d.CompletionStreamAsync(It.IsAny<DriverContext>(), It.IsAny<CompletionRequest>(), It.IsAny<CancellationToken>()))
          .Returns(CompletionChunks(tokens));
      return this;
    }

    /// <summary>Sets up <c>ListEngineModelsAsync</c> to return the given engine models.</summary>
    public MockDriverPack SetupModelEngineModels(params OpenAiModel[] models)
    {
      ModelInferenceDriver
          .Setup(d => d.ListEngineModelsAsync(It.IsAny<DriverContext>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<IList<OpenAiModel>>.Ok([.. models]));
      return this;
    }

    private static async IAsyncEnumerable<string> LogLines(string[] lines)
    {
      await Task.CompletedTask;
      foreach (var line in lines)
        yield return line;
    }

    private static async IAsyncEnumerable<CompletionChunk> CompletionChunks(string[] tokens)
    {
      await Task.CompletedTask;
      foreach (var token in tokens)
      {
        yield return new CompletionChunk
        {
          Id = "cmpl-mock",
          Choices = new List<CompletionChoice> { new() { Index = 0, Text = token } }
        };
      }
    }
  }
}
