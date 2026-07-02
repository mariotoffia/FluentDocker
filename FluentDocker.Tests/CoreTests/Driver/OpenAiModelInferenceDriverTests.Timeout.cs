using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Models;
using FluentDocker.Model.Models.Inference;
using FluentDocker.Tests.Mocks;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver
{
  /// <summary>
  /// MR1: a per-request timeout must be classified distinctly from a caller cancellation
  /// (OperationCanceledException) and from a generic server failure (RequestFailed), on every
  /// non-streaming request path that has the broad catch.
  /// </summary>
  public partial class OpenAiModelInferenceDriverTests
  {
    [Fact]
    public async Task ChatCompletionAsync_ConnectionTimeout_MapsToTimeout_NotRequestFailed()
    {
      // The connection raises TimeoutException (as SendWithTimeoutAsync does on a per-request
      // timeout). It must NOT be collapsed into the broad RequestFailed catch.
      var conn = new MockModelApiConnection().SetupPostThrows("/chat/completions", new TimeoutException("request timed out"));
      var driver = Create(conn);

      var resp = await driver.ChatCompletionAsync(Ctx, new ChatCompletionRequest { Model = "ai/x" }, TestContext.Current.CancellationToken);

      Assert.False(resp.Success);
      Assert.Equal(ErrorCodes.ModelInference.Timeout, resp.ErrorCode);
    }

    [Fact]
    public async Task CompletionAsync_ConnectionTimeout_MapsToTimeout()
    {
      var conn = new MockModelApiConnection().SetupPostThrows("/completions", new TimeoutException("request timed out"));
      var driver = Create(conn);

      var resp = await driver.CompletionAsync(Ctx, new CompletionRequest { Model = "ai/x", Prompt = "p" }, TestContext.Current.CancellationToken);

      Assert.False(resp.Success);
      Assert.Equal(ErrorCodes.ModelInference.Timeout, resp.ErrorCode);
    }

    [Fact]
    public async Task ListEngineModelsAsync_ConnectionTimeout_MapsToTimeout()
    {
      var conn = new MockModelApiConnection().SetupGetThrows("/models", new TimeoutException("request timed out"));
      var driver = Create(conn);

      var resp = await driver.ListEngineModelsAsync(Ctx, TestContext.Current.CancellationToken);

      Assert.False(resp.Success);
      Assert.Equal(ErrorCodes.ModelInference.Timeout, resp.ErrorCode);
    }

    [Fact]
    public async Task ChatCompletionAsync_CallerCancel_ThrowsOperationCanceled_NotTimeout()
    {
      // A caller-initiated cancellation must surface as OperationCanceledException — never be
      // remapped to the Timeout (or RequestFailed) error code.
      var conn = new MockModelApiConnection().SetupPostThrows(
          "/chat/completions", new OperationCanceledException("caller cancelled"));
      var driver = Create(conn);

      await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
          driver.ChatCompletionAsync(Ctx, new ChatCompletionRequest { Model = "ai/x" }, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ChatCompletionAsync_ServerError_StaysRequestFailed_NotTimeout()
    {
      // Guard: a genuine server 500 must remain RequestFailed (not reclassified as Timeout).
      var conn = new MockModelApiConnection().SetupPost("/chat/completions", 500, "boom");
      var driver = Create(conn);

      var resp = await driver.ChatCompletionAsync(Ctx, new ChatCompletionRequest { Model = "ai/x" }, TestContext.Current.CancellationToken);

      Assert.False(resp.Success);
      Assert.Equal(ErrorCodes.ModelInference.RequestFailed, resp.ErrorCode);
    }
  }
}
