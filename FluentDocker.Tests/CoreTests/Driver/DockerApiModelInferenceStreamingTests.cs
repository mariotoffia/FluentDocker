using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers.Docker.Api.Components;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Models;
using FluentDocker.Model.Models.Inference;
using FluentDocker.Tests.Mocks;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver
{
  /// <summary>
  /// Unit tests for SSE streaming on <see cref="DockerApiModelInferenceDriver"/>:
  /// chunk decode, <c>[DONE]</c> termination, malformed-chunk faults and cancellation.
  /// </summary>
  [Trait("Category", "Unit")]
  public class DockerApiModelInferenceStreamingTests
  {
    private static DriverContext Ctx => new("docker");

    private static DockerApiModelInferenceDriver Create(MockModelApiConnection conn) =>
        new(conn, ModelRunnerEndpoint.HostTcp());

    [Fact]
    public async Task ChatCompletionStreamAsync_DecodesRealSse_StopsAtDone()
    {
      var conn = new MockModelApiConnection().SetupStream("/chat/completions", DmrFixtures.Load("chat.sse"));
      var driver = Create(conn);

      var contents = new List<string>();
      await foreach (var chunk in driver.ChatCompletionStreamAsync(Ctx, new ChatCompletionRequest { Model = "ai/smollm2" }, TestContext.Current.CancellationToken))
      {
        var delta = chunk.Choices is { Count: > 0 } ? chunk.Choices[0].Delta?.Content : null;
        if (!string.IsNullOrEmpty(delta))
          contents.Add(delta);
      }

      // real fixture produces several content deltas and terminates cleanly at [DONE]
      Assert.NotEmpty(contents);
    }

    [Fact]
    public async Task ChatCompletionStreamAsync_SetsStreamTrueInRequestBody()
    {
      var conn = new MockModelApiConnection().SetupStream("/chat/completions", "data: [DONE]\n\n");
      var driver = Create(conn);

      await foreach (var _ in driver.ChatCompletionStreamAsync(Ctx, new ChatCompletionRequest { Model = "ai/x" }, TestContext.Current.CancellationToken))
      {
      }

      // The streaming request body is now captured: it must carry stream=true so the
      // server actually streams (and target the chat endpoint).
      var request = conn.GetRequests().Single(r => r.Method == "POST_STREAM");
      Assert.Contains("/chat/completions", request.Path);
      Assert.Contains("\"stream\":true", request.Body.Replace(" ", string.Empty));
    }

    [Fact]
    public async Task ChatCompletionStreamAsync_DoesNotMutateCallersRequest()
    {
      var conn = new MockModelApiConnection().SetupStream("/chat/completions", "data: [DONE]\n\n");
      var driver = Create(conn);

      // Caller leaves Stream unset (null); streaming must force stream=true on the WIRE
      // without touching the caller's instance.
      var request = new ChatCompletionRequest { Model = "ai/x" };

      await foreach (var _ in driver.ChatCompletionStreamAsync(Ctx, request, TestContext.Current.CancellationToken))
      {
      }

      Assert.Null(request.Stream); // caller's object untouched
      var sent = conn.GetRequests().Single(r => r.Method == "POST_STREAM");
      Assert.Contains("\"stream\":true", sent.Body.Replace(" ", string.Empty));
    }

    [Fact]
    public async Task CompletionStreamAsync_DoesNotMutateCallersRequest()
    {
      var conn = new MockModelApiConnection().SetupStream("/completions", "data: [DONE]\n\n");
      var driver = Create(conn);

      var request = new CompletionRequest { Model = "ai/x", Prompt = "p" };

      await foreach (var _ in driver.CompletionStreamAsync(Ctx, request, TestContext.Current.CancellationToken))
      {
      }

      Assert.Null(request.Stream); // caller's object untouched
      var sent = conn.GetRequests().Single(r => r.Method == "POST_STREAM");
      Assert.Contains("\"stream\":true", sent.Body.Replace(" ", string.Empty));
    }

    [Fact]
    public async Task ChatCompletionStreamAsync_MalformedChunk_ThrowsStreamParseError()
    {
      var conn = new MockModelApiConnection().SetupStream("/chat/completions", "data: {not valid json\n\n");
      var driver = Create(conn);

      var ex = await Assert.ThrowsAsync<ModelRunnerException>(async () =>
      {
        await foreach (var _ in driver.ChatCompletionStreamAsync(Ctx, new ChatCompletionRequest { Model = "ai/x" }, TestContext.Current.CancellationToken))
        {
        }
      });

      Assert.Equal(ErrorCodes.ModelInference.StreamParseError, ex.ErrorCode);
    }

    [Fact]
    public async Task ChatCompletionStreamAsync_IgnoresNonDataLines()
    {
      // blank lines and comment lines must be skipped; only data: chunks decode
      const string script = "\n: keep-alive\ndata: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"Hi\"}}]}\n\ndata: [DONE]\n\n";
      var conn = new MockModelApiConnection().SetupStream("/chat/completions", script);
      var driver = Create(conn);

      var contents = new List<string>();
      await foreach (var chunk in driver.ChatCompletionStreamAsync(Ctx, new ChatCompletionRequest { Model = "ai/x" }, TestContext.Current.CancellationToken))
        contents.Add(chunk.Choices[0].Delta.Content);

      Assert.Equal(new[] { "Hi" }, contents);
    }

    [Fact]
    public async Task ChatCompletionStreamAsync_CancelledToken_Throws()
    {
      var conn = new MockModelApiConnection().SetupStream("/chat/completions", DmrFixtures.Load("chat.sse"));
      var driver = Create(conn);

      using var cts = new CancellationTokenSource();
      cts.Cancel();

      await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
      {
        await foreach (var _ in driver.ChatCompletionStreamAsync(Ctx, new ChatCompletionRequest { Model = "ai/x" }, cts.Token))
        {
        }
      });
    }

    [Theory]
    [InlineData(404, ErrorCodes.ModelInference.ModelNotLoaded)]
    [InlineData(401, ErrorCodes.ModelInference.Unauthorized)]
    [InlineData(500, ErrorCodes.ModelInference.RequestFailed)]
    public async Task ChatCompletionStreamAsync_HttpError_ThrowsTypedModelRunnerException(int status, string expectedCode)
    {
      // A streaming request that fails to open (e.g. 404 for a not-pulled model) must
      // surface the SAME typed error as the non-streaming path — not a raw
      // HttpRequestException — so the documented error table holds for streaming too.
      var conn = new MockModelApiConnection().SetupStreamStatus("/chat/completions", status);
      var driver = Create(conn);

      var ex = await Assert.ThrowsAsync<ModelRunnerException>(async () =>
      {
        await foreach (var _ in driver.ChatCompletionStreamAsync(Ctx, new ChatCompletionRequest { Model = "ai/x" }, TestContext.Current.CancellationToken))
        {
        }
      });

      Assert.Equal(expectedCode, ex.ErrorCode);
    }

    [Fact]
    public async Task ChatCompletionStreamAsync_HttpError_PreservesErrorBodyAndInnerException()
    {
      const string body = "{\"error\":{\"message\":\"model 'ai/x' not found\"}}";
      var conn = new MockModelApiConnection().SetupStreamStatus("/chat/completions", 404, body);
      var driver = Create(conn);

      var ex = await Assert.ThrowsAsync<ModelRunnerException>(async () =>
      {
        await foreach (var _ in driver.ChatCompletionStreamAsync(Ctx, new ChatCompletionRequest { Model = "ai/x" }, TestContext.Current.CancellationToken))
        {
        }
      });

      Assert.Equal(ErrorCodes.ModelInference.ModelNotLoaded, ex.ErrorCode);
      Assert.Contains("not found", ex.Message, StringComparison.Ordinal);
      Assert.IsType<HttpRequestException>(ex.InnerException);
    }

    [Fact]
    public async Task CompletionStreamAsync_HttpError_ThrowsTypedModelRunnerException()
    {
      var conn = new MockModelApiConnection().SetupStreamStatus("/completions", 404);
      var driver = Create(conn);

      var ex = await Assert.ThrowsAsync<ModelRunnerException>(async () =>
      {
        await foreach (var _ in driver.CompletionStreamAsync(Ctx, new CompletionRequest { Model = "ai/x", Prompt = "p" }, TestContext.Current.CancellationToken))
        {
        }
      });

      Assert.Equal(ErrorCodes.ModelInference.ModelNotLoaded, ex.ErrorCode);
    }

    [Fact]
    public async Task CompletionStreamAsync_Decodes()
    {
      const string script = "data: {\"choices\":[{\"index\":0,\"text\":\" Pa\"}]}\n\ndata: {\"choices\":[{\"index\":0,\"text\":\"ris\"}]}\n\ndata: [DONE]\n\n";
      var conn = new MockModelApiConnection().SetupStream("/completions", script);
      var driver = Create(conn);

      var text = string.Empty;
      await foreach (var chunk in driver.CompletionStreamAsync(Ctx, new CompletionRequest { Model = "ai/x", Prompt = "p" }, TestContext.Current.CancellationToken))
        text += chunk.Choices[0].Text;

      Assert.Equal(" Paris", text);
    }
  }
}
