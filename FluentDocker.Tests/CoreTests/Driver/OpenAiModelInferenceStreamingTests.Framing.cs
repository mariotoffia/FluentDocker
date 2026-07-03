using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using FluentDocker.Drivers.Models;
using FluentDocker.Model.Models.Inference;
using FluentDocker.Tests.Mocks;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver
{
  /// <summary>
  /// SSE framing / decoding edge cases for <see cref="OpenAiModelInferenceDriver"/> streaming:
  /// a multi-byte codepoint split across a read boundary (M2), CRLF terminators, a trailing
  /// event with no closing blank line, and <c>data:</c> with no space after the colon (M3).
  /// </summary>
  public partial class OpenAiModelInferenceStreamingTests
  {
    private static async Task<List<string>> CollectDeltas(OpenAiModelInferenceDriver driver)
    {
      var contents = new List<string>();
      await foreach (var chunk in driver.ChatCompletionStreamAsync(
          Ctx, new ChatCompletionRequest { Model = "ai/x" }, TestContext.Current.CancellationToken))
        contents.Add(chunk.Choices[0].Delta.Content);
      return contents;
    }

    // ---- M2: a multi-byte UTF-8 codepoint split across two reads must decode intact. ----

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ChatCompletionStreamAsync_MultibyteCodepointSplitAcrossReads_DecodesIntact()
    {
      // "\u00e9" (é) is 0xC3 0xA9 in UTF-8. Deliver the frame so the two bytes land in SEPARATE
      // reads — the split falls mid-codepoint. Only a stateful decoder carried across the read
      // boundary reassembles it; decoding each chunk independently would emit two U+FFFD chars.
      const string frame =
          "data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"\u00e9\"}}]}\n\n" +
          "data: [DONE]\n\n";
      var bytes = Encoding.UTF8.GetBytes(frame);
      var lead = Array.IndexOf(bytes, (byte)0xC3); // first byte of é
      var first = bytes[..(lead + 1)];             // ends mid-codepoint (…0xC3)
      var second = bytes[(lead + 1)..];            // starts with 0xA9…

      var conn = new MockModelApiConnection().SetupStreamByteChunks("/chat/completions", first, second);
      var contents = await CollectDeltas(Create(conn));

      Assert.Equal(new[] { "\u00e9" }, contents); // intact — no U+FFFD, no mojibake
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ChatCompletionStreamAsync_SplitAstralCodepointBeforeFullRead_DecodesIntact()
    {
      const int streamBufferBytes = 4096;
      var tail = new string('a', streamBufferBytes - 1);
      var expected = "\ud83d\ude80" + tail;
      var prefix = Encoding.UTF8.GetBytes("data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"");
      var rocket = Encoding.UTF8.GetBytes("\ud83d\ude80");
      var first = new byte[prefix.Length + 3];
      prefix.CopyTo(first, 0);
      rocket.AsSpan(0, 3).CopyTo(first.AsSpan(prefix.Length));

      var second = new byte[streamBufferBytes];
      second[0] = rocket[3];
      Encoding.UTF8.GetBytes(tail).CopyTo(second, 1);

      var suffix = Encoding.UTF8.GetBytes("\"}}]}\n\ndata: [DONE]\n\n");
      var conn = new MockModelApiConnection().SetupStreamByteChunks("/chat/completions", first, second, suffix);

      Assert.Equal(new[] { expected }, await CollectDeltas(Create(conn)));
    }

    // ---- M3.1: CRLF (\r\n) line terminators frame events with no stray carriage return. ----

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ChatCompletionStreamAsync_CrlfTerminators_DispatchEventsWithoutStrayCr()
    {
      // A CRLF blank line ("\r\n") must still register as the event delimiter and the payload must
      // decode with NO trailing '\r'. A naive '\n'-only splitter would see "\r" as a non-empty line,
      // never fire the delimiter, and collapse both frames + [DONE] into one malformed JSON blob.
      const string script =
          "data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"a\"}}]}\r\n\r\n" +
          "data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"b\"}}]}\r\n\r\n" +
          "data: [DONE]\r\n\r\n";
      var conn = new MockModelApiConnection().SetupStream("/chat/completions", script);

      Assert.Equal(new[] { "a", "b" }, await CollectDeltas(Create(conn)));
    }

    // ---- M3.2: a trailing content frame with NO final blank line before EOF still dispatches. ----

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ChatCompletionStreamAsync_TrailingFrameNoFinalBlankLine_StillDispatched()
    {
      // The last event carries no closing blank line and the stream just ends — the EOF flush
      // (line-level then event-level) must still surface it, otherwise the final delta is lost.
      const string script = "data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"end\"}}]}";
      var conn = new MockModelApiConnection().SetupStream("/chat/completions", script);

      Assert.Equal(new[] { "end" }, await CollectDeltas(Create(conn)));
    }

    // ---- M3.3: "data:{json}" with no space after the colon parses (the space is optional). ----

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ChatCompletionStreamAsync_DataColonNoSpace_ParsesAsValidEvent()
    {
      // Per the SSE spec the single leading space after "data:" is OPTIONAL; a server that omits it
      // must still parse. Stripping a fixed-width prefix that assumes the space would corrupt the
      // JSON (drop its leading '{') and fail.
      const string script =
          "data:{\"choices\":[{\"index\":0,\"delta\":{\"content\":\"x\"}}]}\n\n" +
          "data:[DONE]\n\n";
      var conn = new MockModelApiConnection().SetupStream("/chat/completions", script);

      Assert.Equal(new[] { "x" }, await CollectDeltas(Create(conn)));
    }
  }
}
