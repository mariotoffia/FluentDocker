using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers.Models;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Models;
using FluentDocker.Model.Models.Inference;
using FluentDocker.Tests.Mocks;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver
{
  /// <summary>
  /// Unit tests for SSE streaming on <see cref="OpenAiModelInferenceDriver"/>:
  /// chunk decode, <c>[DONE]</c> termination, malformed-chunk faults and cancellation.
  /// </summary>
  [Trait("Category", "Unit")]
  public partial class OpenAiModelInferenceStreamingTests
  {
    private static DriverContext Ctx => new("docker");

    private static OpenAiModelInferenceDriver Create(MockModelApiConnection conn) =>
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
      Assert.Contains("\"stream\":true", request.Body!.Replace(" ", string.Empty));
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
      Assert.Contains("\"stream\":true", sent.Body!.Replace(" ", string.Empty));
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
      Assert.Contains("\"stream\":true", sent.Body!.Replace(" ", string.Empty));
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

    // ---- A6/NEW1: a mid-stream SSE error frame must throw, carrying the server's message ----

    [Fact]
    public async Task ChatCompletionStreamAsync_MidStreamErrorFrame_ThrowsWithMessage_AfterYieldingPriorFrames()
    {
      const string script =
          "data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"Hi\"}}]}\n\n" +
          "data: {\"error\":{\"message\":\"context length exceeded\"}}\n\n" +
          "data: [DONE]\n\n";
      var conn = new MockModelApiConnection().SetupStream("/chat/completions", script);
      var driver = Create(conn);

      var contents = new List<string>();
      var ex = await Assert.ThrowsAsync<ModelRunnerException>(async () =>
      {
        await foreach (var chunk in driver.ChatCompletionStreamAsync(Ctx, new ChatCompletionRequest { Model = "ai/x" }, TestContext.Current.CancellationToken))
          contents.Add(chunk.Choices[0].Delta.Content);
      });

      Assert.Equal(ErrorCodes.ModelInference.RequestFailed, ex.ErrorCode);
      Assert.Contains("context length exceeded", ex.Message, StringComparison.Ordinal);
      Assert.Equal(new[] { "Hi" }, contents); // the valid frame before the error was yielded
    }

    [Fact]
    public async Task CompletionStreamAsync_MidStreamErrorFrame_Throws()
    {
      const string script =
          "data: {\"choices\":[{\"index\":0,\"text\":\"Pa\"}]}\n\n" +
          "data: {\"error\":\"boom\"}\n\n";
      var conn = new MockModelApiConnection().SetupStream("/completions", script);
      var driver = Create(conn);

      var ex = await Assert.ThrowsAsync<ModelRunnerException>(async () =>
      {
        await foreach (var _ in driver.CompletionStreamAsync(Ctx, new CompletionRequest { Model = "ai/x", Prompt = "p" }, TestContext.Current.CancellationToken))
        {
        }
      });

      Assert.Equal(ErrorCodes.ModelInference.RequestFailed, ex.ErrorCode);
      Assert.Contains("boom", ex.Message, StringComparison.Ordinal);
    }

    // ---- NEW5: an empty `data:` line must be skipped, not abort the stream ----

    [Fact]
    public async Task ChatCompletionStreamAsync_EmptyDataLine_IsSkipped_StreamCompletes()
    {
      // An interleaved blank `data:` frame (an SSE heartbeat) must NOT abort the stream.
      const string script =
          "data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"Hel\"}}]}\n\n" +
          "data: \n\n" +
          "data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"lo\"}}]}\n\n" +
          "data: [DONE]\n\n";
      var conn = new MockModelApiConnection().SetupStream("/chat/completions", script);
      var driver = Create(conn);

      var text = string.Empty;
      await foreach (var chunk in driver.ChatCompletionStreamAsync(Ctx, new ChatCompletionRequest { Model = "ai/x" }, TestContext.Current.CancellationToken))
        text += chunk.Choices[0].Delta.Content;

      Assert.Equal("Hello", text);
    }

    // ---- H7/M1: a single SSE line exceeding the byte cap must throw a typed exception ----

    [Fact]
    public async Task ChatCompletionStreamAsync_OversizedSseLine_ThrowsStreamParseError()
    {
      // One `data:` line padded well beyond the 1 MiB cap (no newline within it).
      var huge = new string('x', (1024 * 1024) + 16);
      var script = "data: " + huge + "\n\ndata: [DONE]\n\n";
      var conn = new MockModelApiConnection().SetupStream("/chat/completions", script);
      var driver = Create(conn);

      var ex = await Assert.ThrowsAsync<ModelRunnerException>(async () =>
      {
        await foreach (var _ in driver.ChatCompletionStreamAsync(Ctx, new ChatCompletionRequest { Model = "ai/x" }, TestContext.Current.CancellationToken))
        {
        }
      });

      Assert.Equal(ErrorCodes.ModelInference.StreamParseError, ex.ErrorCode);
    }

    // ---- TEST-2: a single SSE frame split across two underlying reads must be reassembled. ----

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ChatCompletionStreamAsync_FrameSplitAcrossReads_IsReassembledIntoOneChunk()
    {
      // The first read returns a partial `data:` frame (no newline yet); the second returns the
      // rest plus the terminator. The driver must stitch them into one intact chunk.
      const string chunk1 = "data: {\"choices\":[{\"index\":0,\"delta\":{\"con";
      const string chunk2 = "tent\":\"Hello\"}}]}\n\ndata: [DONE]\n\n";
      var conn = new MockModelApiConnection().SetupStreamChunks("/chat/completions", chunk1, chunk2);
      var driver = Create(conn);

      var contents = new List<string>();
      await foreach (var chunk in driver.ChatCompletionStreamAsync(Ctx, new ChatCompletionRequest { Model = "ai/x" }, TestContext.Current.CancellationToken))
      {
        var delta = chunk.Choices is { Count: > 0 } ? chunk.Choices[0].Delta?.Content : null;
        if (!string.IsNullOrEmpty(delta))
          contents.Add(delta);
      }

      Assert.Equal(new[] { "Hello" }, contents);
    }

    // ---- TEST-6: a completion stream closed mid-stream (IOException after a valid prefix
    // frame) must yield the prefix then throw a typed ModelRunnerException(EndpointUnreachable). ----

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CompletionStreamAsync_StreamClosedMidStream_ThrowsEndpointUnreachable_AfterPrefix()
    {
      const string prefix = "data: {\"choices\":[{\"index\":0,\"text\":\"Pa\"}]}\n\n";
      var conn = new MockModelApiConnection().SetupStreamFault("/completions", prefix, prefix.Length);
      var driver = Create(conn);

      var texts = new List<string>();
      var ex = await Assert.ThrowsAsync<ModelRunnerException>(async () =>
      {
        await foreach (var chunk in driver.CompletionStreamAsync(Ctx, new CompletionRequest { Model = "ai/x", Prompt = "p" }, TestContext.Current.CancellationToken))
          texts.Add(chunk.Choices[0].Text);
      });

      Assert.Equal(ErrorCodes.ModelInference.EndpointUnreachable, ex.ErrorCode);
      Assert.Equal(new[] { "Pa" }, texts); // the valid prefix frame was yielded before the fault
    }

    // ---- MR4: a single JSON event split across TWO data: lines must reassemble into one object. ----

    [Fact]
    public async Task ChatCompletionStreamAsync_EventSplitAcrossTwoDataLines_ParsesAsOneObject()
    {
      // Standards-compliant SSE may split ONE JSON event across multiple data: lines; the joined
      // payload (LF-joined) is the event. The old per-line-parse treated each data: line as a whole
      // JSON payload and would fail the first partial line as malformed.
      const string script =
          "data: {\"choices\":[{\"index\":0,\n" +
          "data: \"delta\":{\"content\":\"Hi\"}}]}\n\n" +
          "data: [DONE]\n\n";
      var conn = new MockModelApiConnection().SetupStream("/chat/completions", script);
      var driver = Create(conn);

      var contents = new List<string>();
      await foreach (var chunk in driver.ChatCompletionStreamAsync(Ctx, new ChatCompletionRequest { Model = "ai/x" }, TestContext.Current.CancellationToken))
        contents.Add(chunk.Choices[0].Delta.Content);

      Assert.Equal(new[] { "Hi" }, contents); // the two data: lines joined into one valid event
    }

    [Fact]
    public async Task ChatCompletionStreamAsync_MultipleEventsSeparatedByBlankLines_EachDispatchOnce()
    {
      // Blank lines are event delimiters: two events must dispatch exactly once each, then [DONE].
      const string script =
          "data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"a\"}}]}\n\n" +
          "data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"b\"}}]}\n\n" +
          "data: [DONE]\n\n";
      var conn = new MockModelApiConnection().SetupStream("/chat/completions", script);
      var driver = Create(conn);

      var contents = new List<string>();
      await foreach (var chunk in driver.ChatCompletionStreamAsync(Ctx, new ChatCompletionRequest { Model = "ai/x" }, TestContext.Current.CancellationToken))
        contents.Add(chunk.Choices[0].Delta.Content);

      Assert.Equal(new[] { "a", "b" }, contents);
    }

    // ---- MR5: idle timeout is applied per READ (bounded allocations) and fires only on a stall. ----

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ChatCompletionStreamAsync_StalledStream_FiresIdleTimeout_AsEndpointUnreachable()
    {
      // A server that stops sending must trip the configured idle timeout, surfaced as the
      // deliberate EndpointUnreachable classification (C12) — not a hang, and not per-character
      // timers. This exercises the ONE-idle-window-per-chunked-read path.
      var conn = new MockModelApiConnection().SetupStreamStalling("/chat/completions");
      conn.StreamReadIdleTimeout = TimeSpan.FromMilliseconds(150);
      var driver = Create(conn);

      var ex = await Assert.ThrowsAsync<ModelRunnerException>(async () =>
      {
        await foreach (var _ in driver.ChatCompletionStreamAsync(Ctx, new ChatCompletionRequest { Model = "ai/x" }, TestContext.Current.CancellationToken))
        {
        }
      });

      Assert.Equal(ErrorCodes.ModelInference.EndpointUnreachable, ex.ErrorCode);
      Assert.Contains("idle timeout", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ChatCompletionStreamAsync_StallAfterPrefix_YieldsPrefix_ThenIdleTimeout()
    {
      // The idle window RESETS on each successful read: the prefix frame is delivered and yielded,
      // and only the subsequent silence trips the timeout — proving the timer is per-read.
      var conn = new MockModelApiConnection().SetupStreamStalling(
          "/chat/completions", "data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"Hi\"}}]}\n\n");
      conn.StreamReadIdleTimeout = TimeSpan.FromMilliseconds(150);
      var driver = Create(conn);

      var contents = new List<string>();
      var ex = await Assert.ThrowsAsync<ModelRunnerException>(async () =>
      {
        await foreach (var chunk in driver.ChatCompletionStreamAsync(Ctx, new ChatCompletionRequest { Model = "ai/x" }, TestContext.Current.CancellationToken))
          contents.Add(chunk.Choices[0].Delta.Content);
      });

      Assert.Equal(ErrorCodes.ModelInference.EndpointUnreachable, ex.ErrorCode);
      Assert.Equal(new[] { "Hi" }, contents); // prefix frame arrived before the stall
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ChatCompletionStreamAsync_SteadyStream_WithIdleTimeout_DoesNotFalselyTimeout()
    {
      // A healthy stream that completes must NOT trip the idle timeout even when one is configured.
      var conn = new MockModelApiConnection().SetupStream(
          "/chat/completions",
          "data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"Hi\"}}]}\n\ndata: [DONE]\n\n");
      conn.StreamReadIdleTimeout = TimeSpan.FromMilliseconds(500);
      var driver = Create(conn);

      var contents = new List<string>();
      await foreach (var chunk in driver.ChatCompletionStreamAsync(Ctx, new ChatCompletionRequest { Model = "ai/x" }, TestContext.Current.CancellationToken))
        contents.Add(chunk.Choices[0].Delta.Content);

      Assert.Equal(new[] { "Hi" }, contents);
    }
  }
}
