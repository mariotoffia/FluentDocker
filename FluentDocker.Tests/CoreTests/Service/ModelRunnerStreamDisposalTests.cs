using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Models;
using FluentDocker.Model.Models.Inference;
using FluentDocker.Services;
using FluentDocker.Services.Impl;
using FluentDocker.Tests.Mocks;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Service
{
  /// <summary>
  /// D-M2: a runner (or its owned connection) disposed WHILE a consumer is still iterating a
  /// stream must surface a typed <see cref="ModelRunnerException"/> (<see cref="ErrorCodes.ModelInference.Disposed"/>),
  /// never a raw <see cref="ObjectDisposedException"/> from inside the driver. Every scenario
  /// (mid-enumeration disposal, underlying-ODE translation, eager already-disposed check, and the
  /// unaffected happy path) runs against the full cross-product of the four streaming call sites
  /// (<see cref="GenericOpenAiModelRunner"/> and <see cref="ModelRunnerService"/>, chat + completion),
  /// all backed by the shared <c>ModelRunnerInferenceHelpers.GuardDisposalAsync</c> wrapper.
  /// </summary>
  [Trait("Category", "Unit")]
  public class ModelRunnerStreamDisposalTests
  {
    public enum Site
    {
      GenericChat,
      GenericCompletion,
      ServiceChat,
      ServiceCompletion
    }

    [Theory]
    [InlineData(Site.GenericChat)]
    [InlineData(Site.GenericCompletion)]
    [InlineData(Site.ServiceChat)]
    [InlineData(Site.ServiceCompletion)]
    public async Task Stream_DisposedMidEnumeration_ThrowsModelRunnerExceptionDisposed(Site site)
    {
      var driver = new GatedStreamingInferenceDriver();
      // The driver is ALSO the owned resource, so runner.DisposeAsync() disposes it — exactly like a
      // real owned connection whose disposal makes the in-flight HttpClient enumeration throw ODE.
      var (runner, kernel) = await CreateRunnerAsync(site, driver, ownsDriver: true);
      await using var _ = kernel;
      var ct = TestContext.Current.CancellationToken;

      await using var enumerator = StreamTexts(runner, site, ct).GetAsyncEnumerator(ct);
      Assert.True(await enumerator.MoveNextAsync()); // chunk 1 arrives before disposal.

      await ((IAsyncDisposable)runner).DisposeAsync();

      var ex = await Assert.ThrowsAsync<ModelRunnerException>(async () =>
      {
        await enumerator.MoveNextAsync();
      });
      Assert.Equal(ErrorCodes.ModelInference.Disposed, ex.ErrorCode);
      Assert.IsNotType<ObjectDisposedException>(ex);
    }

    [Theory]
    [InlineData(Site.GenericChat)]
    [InlineData(Site.GenericCompletion)]
    [InlineData(Site.ServiceChat)]
    [InlineData(Site.ServiceCompletion)]
    public async Task Stream_UnderlyingObjectDisposedException_TranslatedWithInnerException(Site site)
    {
      // The runner is NOT disposed; the driver's own enumeration throws ODE mid-flight (as a live
      // HttpClient would). The per-MoveNext catch must translate it into the typed Disposed error.
      var driver = new GatedStreamingInferenceDriver { ThrowDisposedAfterGate = true };
      var (runner, kernel) = await CreateRunnerAsync(site, driver, ownsDriver: false);
      await using var _ = kernel;
      await using var __ = (IAsyncDisposable)runner;
      var ct = TestContext.Current.CancellationToken;

      await using var enumerator = StreamTexts(runner, site, ct).GetAsyncEnumerator(ct);
      Assert.True(await enumerator.MoveNextAsync()); // chunk 1; runner is NOT disposed here.

      driver.ReleaseGate(); // let the fake's own MoveNextAsync throw ODE on resume.
      var ex = await Assert.ThrowsAsync<ModelRunnerException>(async () =>
      {
        await enumerator.MoveNextAsync();
      });

      Assert.Equal(ErrorCodes.ModelInference.Disposed, ex.ErrorCode);
      Assert.IsType<ObjectDisposedException>(ex.InnerException);
    }

    [Theory]
    [InlineData(Site.GenericChat)]
    [InlineData(Site.GenericCompletion)]
    [InlineData(Site.ServiceChat)]
    [InlineData(Site.ServiceCompletion)]
    public async Task Stream_DisposedBeforeCall_StillThrowsObjectDisposedExceptionSynchronously(Site site)
    {
      var driver = new GatedStreamingInferenceDriver();
      var (runner, kernel) = await CreateRunnerAsync(site, driver, ownsDriver: false);
      await using var _ = kernel;
      await ((IAsyncDisposable)runner).DisposeAsync();

      // Eager call-time ThrowIfDisposed() is preserved: calling a stream method on an already-disposed
      // runner throws ObjectDisposedException synchronously (direct misuse), NOT the typed Disposed error.
      await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
      {
        await foreach (var __ in StreamTexts(runner, site, TestContext.Current.CancellationToken))
        {
        }
      });
    }

    [Theory]
    [InlineData(Site.GenericChat)]
    [InlineData(Site.GenericCompletion)]
    [InlineData(Site.ServiceChat)]
    [InlineData(Site.ServiceCompletion)]
    public async Task Stream_NotDisposed_YieldsAllChunksUnaffected(Site site)
    {
      var driver = new GatedStreamingInferenceDriver();
      driver.ReleaseGate(); // stream runs straight through, like the pre-guard behavior.
      var (runner, kernel) = await CreateRunnerAsync(site, driver, ownsDriver: false);
      await using var _ = kernel;
      await using var __ = (IAsyncDisposable)runner;

      var texts = new List<string>();
      await foreach (var text in StreamTexts(runner, site, TestContext.Current.CancellationToken))
        texts.Add(text);

      Assert.Equal(new[] { "one", "two" }, texts);
    }

    /// <summary>
    /// Builds the runner under test for <paramref name="site"/>. Service sites need a live mock
    /// kernel (returned for the caller to dispose; null for the Generic sites, which
    /// <c>await using</c> tolerates). With <paramref name="ownsDriver"/> the driver doubles as
    /// the runner's owned resource so disposing the runner faults the in-flight stream.
    /// </summary>
    private static async Task<(IModelInference Runner, FluentDockerKernel? Kernel)> CreateRunnerAsync(
        Site site, GatedStreamingInferenceDriver driver, bool ownsDriver)
    {
      var owned = ownsDriver ? driver : null;
      if (site is Site.GenericChat or Site.GenericCompletion)
      {
        return (new GenericOpenAiModelRunner(
            ModelRunnerEndpoint.HostTcp(), ModelReference.Parse("ai/smollm2"), driver, ownedResource: owned!), null);
      }

      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync("docker", new MockDriverPack());
      return (new ModelRunnerService(
          kernel, "docker", ModelRunnerEndpoint.HostTcp(), ModelReference.Parse("ai/smollm2"), driver, ownedResource: owned!), kernel);
    }

    private static async IAsyncEnumerable<string> StreamTexts(
        IModelInference runner, Site site, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
      if (site is Site.GenericChat or Site.ServiceChat)
      {
        await foreach (var chunk in runner.ChatCompletionStreamAsync(
            new ChatCompletionRequest { Model = "ai/smollm2" }, cancellationToken))
          yield return chunk.Choices![0].Delta!.Content!; // fake always populates these
      }
      else
      {
        await foreach (var chunk in runner.CompletionStreamAsync(
            new CompletionRequest { Model = "ai/smollm2" }, cancellationToken))
          yield return chunk.Choices![0].Text!; // fake always populates these
      }
    }

    /// <summary>
    /// Hand-written (non-Moq) <see cref="IModelInferenceDriver"/> fake whose streaming methods
    /// yield one chunk, then suspend on a <see cref="TaskCompletionSource"/> gate. Disposing it
    /// (as the runner's owned resource) faults the gate with <see cref="ObjectDisposedException"/>,
    /// mirroring a real owned connection whose disposal makes the in-flight enumeration throw ODE.
    /// Setting <see cref="ThrowDisposedAfterGate"/> instead forces that ODE on the next resume with
    /// the runner still live, exercising the per-MoveNext translation catch.
    /// </summary>
    private sealed class GatedStreamingInferenceDriver : IModelInferenceDriver, IAsyncDisposable
    {
      private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

      public bool ThrowDisposedAfterGate { get; set; }

      public void ReleaseGate() => _gate.TrySetResult();

      public ValueTask DisposeAsync()
      {
        _gate.TrySetException(new ObjectDisposedException(nameof(GatedStreamingInferenceDriver)));
        return ValueTask.CompletedTask;
      }

      public async IAsyncEnumerable<ChatCompletionChunk> ChatCompletionStreamAsync(
          DriverContext context, ChatCompletionRequest request,
          [EnumeratorCancellation] CancellationToken cancellationToken = default)
      {
        yield return new ChatCompletionChunk
        {
          Choices = new List<ChatChunkChoice> { new() { Delta = new ChatMessage { Content = "one" } } }
        };
        await _gate.Task.ConfigureAwait(false); // faults with ODE once disposed / released-to-throw.
        ObjectDisposedException.ThrowIf(ThrowDisposedAfterGate, this);
        yield return new ChatCompletionChunk
        {
          Choices = new List<ChatChunkChoice> { new() { Delta = new ChatMessage { Content = "two" } } }
        };
      }

      public async IAsyncEnumerable<CompletionChunk> CompletionStreamAsync(
          DriverContext context, CompletionRequest request,
          [EnumeratorCancellation] CancellationToken cancellationToken = default)
      {
        yield return new CompletionChunk { Choices = new List<CompletionChoice> { new() { Text = "one" } } };
        await _gate.Task.ConfigureAwait(false);
        ObjectDisposedException.ThrowIf(ThrowDisposedAfterGate, this);
        yield return new CompletionChunk { Choices = new List<CompletionChoice> { new() { Text = "two" } } };
      }

      public Task<CommandResponse<ChatCompletionResponse>> ChatCompletionAsync(
          DriverContext context, ChatCompletionRequest request, CancellationToken cancellationToken = default) =>
          throw new NotSupportedException("Not exercised by streaming-disposal tests.");

      public Task<CommandResponse<CompletionResponse>> CompletionAsync(
          DriverContext context, CompletionRequest request, CancellationToken cancellationToken = default) =>
          throw new NotSupportedException("Not exercised by streaming-disposal tests.");

      public Task<CommandResponse<EmbeddingsResponse>> EmbeddingsAsync(
          DriverContext context, EmbeddingsRequest request, CancellationToken cancellationToken = default) =>
          throw new NotSupportedException("Not exercised by streaming-disposal tests.");

      public Task<CommandResponse<IList<OpenAiModel>>> ListEngineModelsAsync(
          DriverContext context, CancellationToken cancellationToken = default) =>
          throw new NotSupportedException("Not exercised by streaming-disposal tests.");
    }
  }
}
