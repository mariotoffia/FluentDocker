using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Common;
using FluentDocker.Drivers.Models;
using FluentDocker.Drivers.Models.Connection;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Models;
using FluentDocker.Model.Models.Inference;
using FluentDocker.Tests.Mocks;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver
{
  /// <summary>
  /// Unit tests for <see cref="OpenAiModelInferenceDriver"/> (non-streaming)
  /// driven by <see cref="MockModelApiConnection"/> and real DMR payloads.
  /// </summary>
  [Trait("Category", "Unit")]
  public partial class OpenAiModelInferenceDriverTests
  {
    private static DriverContext Ctx => new("docker");

    private static OpenAiModelInferenceDriver Create(MockModelApiConnection conn) =>
        new(conn, ModelRunnerEndpoint.HostTcp());

    [Fact]
    public async Task ChatCompletionAsync_PostsAndParses()
    {
      var conn = new MockModelApiConnection().SetupPost("/chat/completions", 200, DmrFixtures.Load("chat.json"));
      var driver = Create(conn);

      var resp = await driver.ChatCompletionAsync(Ctx, new ChatCompletionRequest
      {
        Model = "ai/smollm2",
        Messages = new List<ChatMessage> { new() { Role = "user", Content = "hi" } }
      }, TestContext.Current.CancellationToken);

      Assert.True(resp.Success);
      Assert.False(string.IsNullOrEmpty(resp.Data.Choices[0].Message.Content));

      var request = conn.GetRequests().Single(r => r.Method == "POST");
      Assert.Contains("/engines/llama.cpp/v1/chat/completions", request.Path);
      Assert.Contains("\"stream\":false", request.Body); // forced non-stream
      Assert.Contains("ai/smollm2", request.Body);
    }

    [Fact]
    public async Task ChatCompletionAsync_DoesNotMutateCallersRequest()
    {
      var conn = new MockModelApiConnection().SetupPost("/chat/completions", 200, DmrFixtures.Load("chat.json"));
      var driver = Create(conn);

      // Caller leaves Stream unset (null); the driver must force stream=false on the
      // WIRE without touching the caller's instance.
      var request = new ChatCompletionRequest
      {
        Model = "ai/smollm2",
        Messages = new List<ChatMessage> { new() { Role = "user", Content = "hi" } }
      };

      await driver.ChatCompletionAsync(Ctx, request, TestContext.Current.CancellationToken);

      Assert.Null(request.Stream); // caller's object untouched
      var sent = conn.GetRequests().Single(r => r.Method == "POST");
      Assert.Contains("\"stream\":false", sent.Body!.Replace(" ", string.Empty));
    }

    [Fact]
    public async Task CompletionAsync_DoesNotMutateCallersRequest()
    {
      var conn = new MockModelApiConnection().SetupPost("/completions", 200, DmrFixtures.Load("completion.json"));
      var driver = Create(conn);

      var request = new CompletionRequest { Model = "ai/smollm2", Prompt = "x" };

      await driver.CompletionAsync(Ctx, request, TestContext.Current.CancellationToken);

      Assert.Null(request.Stream); // caller's object untouched
      var sent = conn.GetRequests().Single(r => r.Method == "POST");
      Assert.Contains("\"stream\":false", sent.Body!.Replace(" ", string.Empty));
    }

    [Fact]
    public async Task CompletionAsync_PostsAndParses()
    {
      var conn = new MockModelApiConnection().SetupPost("/completions", 200, DmrFixtures.Load("completion.json"));
      var driver = Create(conn);

      var resp = await driver.CompletionAsync(Ctx, new CompletionRequest { Model = "ai/smollm2", Prompt = "x" },
          TestContext.Current.CancellationToken);

      Assert.True(resp.Success);
      Assert.Equal("text_completion", resp.Data.Object);
    }

    [Fact]
    public async Task EmbeddingsAsync_PostsAndParses()
    {
      var conn = new MockModelApiConnection().SetupPost("/embeddings", 200, DmrFixtures.Load("embeddings.json"));
      var driver = Create(conn);

      var resp = await driver.EmbeddingsAsync(Ctx, new EmbeddingsRequest { Model = "ai/embeddinggemma", Input = new List<string> { "hi" } },
          TestContext.Current.CancellationToken);

      Assert.True(resp.Success);
      Assert.NotEmpty(resp.Data.Data[0].Embedding);
    }

    [Fact]
    public async Task ListEngineModelsAsync_GetsAndParses()
    {
      const string models = "{\"object\":\"list\",\"data\":[{\"id\":\"docker.io/ai/smollm2:latest\",\"object\":\"model\",\"owned_by\":\"docker\"}]}";
      var conn = new MockModelApiConnection().SetupGet("/models", 200, models);
      var driver = Create(conn);

      var resp = await driver.ListEngineModelsAsync(Ctx, TestContext.Current.CancellationToken);

      Assert.True(resp.Success);
      Assert.Single(resp.Data);
      Assert.Equal("docker.io/ai/smollm2:latest", resp.Data[0].Id);
    }

    [Fact]
    public async Task ChatCompletionAsync_404_MapsToModelNotLoaded()
    {
      var conn = new MockModelApiConnection().SetupPost("/chat/completions", 404, "error while getting model: model not found");
      var driver = Create(conn);

      var resp = await driver.ChatCompletionAsync(Ctx, new ChatCompletionRequest { Model = "ai/nope" }, TestContext.Current.CancellationToken);

      Assert.False(resp.Success);
      Assert.Equal(ErrorCodes.ModelInference.ModelNotLoaded, resp.ErrorCode);
      Assert.Contains("ai/nope", resp.Error, StringComparison.Ordinal);
      Assert.Contains("endpoint/base path", resp.Error, StringComparison.OrdinalIgnoreCase);
    }

    // Captured from live Docker Model Runner (llama.cpp backend) on 2026-07-03:
    //   POST /engines/v1/chat/completions {"model":"ai/does-not-exist-xyz",...} -> 404
    //     error while getting model: get model '"ai/does-not-exist-xyz"': model not found
    //   GET /bogus/path -> 404
    //     not found
    // The body heuristic must map the first to ModelNotLoaded and the second (route miss,
    // no "model" mention) to RequestFailed.
    [Theory]
    [InlineData("error while getting model: get model '\"ai/does-not-exist-xyz\"': model not found",
        ErrorCodes.ModelInference.ModelNotLoaded)]
    [InlineData("not found", ErrorCodes.ModelInference.RequestFailed)]
    public async Task ChatCompletionAsync_404_RealDmrBodies_DisambiguateModelVsRoute(
        string body, string expectedCode)
    {
      var conn = new MockModelApiConnection().SetupPost("/chat/completions", 404, body);
      var driver = Create(conn);

      var resp = await driver.ChatCompletionAsync(Ctx, new ChatCompletionRequest { Model = "ai/does-not-exist-xyz" }, TestContext.Current.CancellationToken);

      Assert.False(resp.Success);
      Assert.Equal(expectedCode, resp.ErrorCode);
    }

    [Fact]
    public async Task ChatCompletionAsync_401_MapsToUnauthorized()
    {
      var conn = new MockModelApiConnection().SetupPost("/chat/completions", 401, "{\"error\":\"unauthorized\"}");
      var driver = Create(conn);

      var resp = await driver.ChatCompletionAsync(Ctx, new ChatCompletionRequest { Model = "ai/x" }, TestContext.Current.CancellationToken);

      Assert.False(resp.Success);
      Assert.Equal(ErrorCodes.ModelInference.Unauthorized, resp.ErrorCode);
    }

    [Fact]
    public async Task ChatCompletionAsync_500_MapsToRequestFailed()
    {
      var conn = new MockModelApiConnection().SetupPost("/chat/completions", 500, "boom");
      var driver = Create(conn);

      var resp = await driver.ChatCompletionAsync(Ctx, new ChatCompletionRequest { Model = "ai/x" }, TestContext.Current.CancellationToken);

      Assert.False(resp.Success);
      Assert.Equal(ErrorCodes.ModelInference.RequestFailed, resp.ErrorCode);
      Assert.NotNull(resp.ErrorContext);
    }

    // ---- C11: a literal JSON `null` body must return a failed CommandResponse, never throw ----

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ChatCompletionAsync_NullBody_ReturnsFail_NotThrow()
    {
      var conn = new MockModelApiConnection().SetupPost("/chat/completions", 200, "null");
      var driver = Create(conn);

      var resp = await driver.ChatCompletionAsync(Ctx, new ChatCompletionRequest { Model = "ai/x" }, TestContext.Current.CancellationToken);

      Assert.False(resp.Success);
      Assert.Equal(ErrorCodes.ModelInference.StreamParseError, resp.ErrorCode);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task CompletionAsync_NullBody_ReturnsFail_NotThrow()
    {
      var conn = new MockModelApiConnection().SetupPost("/completions", 200, "null");
      var driver = Create(conn);

      var resp = await driver.CompletionAsync(Ctx, new CompletionRequest { Model = "ai/x", Prompt = "p" }, TestContext.Current.CancellationToken);

      Assert.False(resp.Success);
      Assert.Equal(ErrorCodes.ModelInference.StreamParseError, resp.ErrorCode);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task EmbeddingsAsync_NullBody_ReturnsFail_NotThrow()
    {
      var conn = new MockModelApiConnection().SetupPost("/embeddings", 200, "null");
      var driver = Create(conn);

      var resp = await driver.EmbeddingsAsync(Ctx, new EmbeddingsRequest { Model = "ai/x", Input = new List<string> { "hi" } }, TestContext.Current.CancellationToken);

      Assert.False(resp.Success);
      Assert.Equal(ErrorCodes.ModelInference.StreamParseError, resp.ErrorCode);
    }

    // ---- Item 4: a malformed (non-JSON) body must classify as a distinct parse error
    // (StreamParseError) instead of being swallowed into a generic transport RequestFailed ----

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ChatCompletionAsync_MalformedJson_ClassifiesAsParseError_NotRequestFailed()
    {
      var conn = new MockModelApiConnection().SetupPost("/chat/completions", 200, "{ not valid json");
      var driver = Create(conn);

      var resp = await driver.ChatCompletionAsync(Ctx, new ChatCompletionRequest { Model = "ai/x" }, TestContext.Current.CancellationToken);

      Assert.False(resp.Success);
      Assert.Equal(ErrorCodes.ModelInference.StreamParseError, resp.ErrorCode);
      Assert.NotEqual(ErrorCodes.ModelInference.RequestFailed, resp.ErrorCode);
      Assert.Contains("malformed", resp.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task EmbeddingsAsync_MalformedJson_ClassifiesAsParseError_NotRequestFailed()
    {
      var conn = new MockModelApiConnection().SetupPost("/embeddings", 200, "<html>not json</html>");
      var driver = Create(conn);

      var resp = await driver.EmbeddingsAsync(Ctx, new EmbeddingsRequest { Model = "ai/x", Input = new List<string> { "hi" } }, TestContext.Current.CancellationToken);

      Assert.False(resp.Success);
      Assert.Equal(ErrorCodes.ModelInference.StreamParseError, resp.ErrorCode);
      Assert.NotEqual(ErrorCodes.ModelInference.RequestFailed, resp.ErrorCode);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public async Task ChatCompletionAsync_200ErrorEnvelope_ReturnsRequestFailed()
    {
      const string body = "{\"error\":{\"message\":\"context length exceeded\"}}";
      var conn = new MockModelApiConnection().SetupPost("/chat/completions", 200, body);
      var driver = Create(conn);

      var resp = await driver.ChatCompletionAsync(Ctx, new ChatCompletionRequest { Model = "ai/x" }, TestContext.Current.CancellationToken);

      Assert.False(resp.Success);
      Assert.Equal(ErrorCodes.ModelInference.RequestFailed, resp.ErrorCode);
      Assert.Contains("context length exceeded", resp.Error, StringComparison.Ordinal);
    }

    // ---- NEW6: copy constructors must preserve every property and be independent of the source ----

    [Fact]
    public void ChatCompletionRequest_Copy_PreservesEveryPublicSettableProperty()
    {
      var original = new ChatCompletionRequest
      {
        Model = "ai/smollm2",
        Messages = new List<ChatMessage> { new() { Role = "user", Content = "hi", Name = "n" } },
        MaxTokens = 42,
        Temperature = 0.7,
        TopP = 0.9,
        Stream = true,
        Stop = new List<string> { "STOP" },
        PresencePenalty = 0.1,
        FrequencyPenalty = 0.2,
        Seed = 123
      };

      var copy = new ChatCompletionRequest(original);

      AssertAllSettablePropertiesEqualByJson(original, copy);
    }

    [Fact]
    public void CompletionRequest_Copy_PreservesEveryPublicSettableProperty()
    {
      var original = new CompletionRequest
      {
        Model = "ai/smollm2",
        Prompt = "once upon a time",
        MaxTokens = 42,
        Temperature = 0.7,
        TopP = 0.9,
        Stream = true,
        Stop = new List<string> { "STOP" },
        Seed = 123
      };

      var copy = new CompletionRequest(original);

      AssertAllSettablePropertiesEqualByJson(original, copy);
    }

    [Fact]
    public void ChatCompletionRequest_Copy_DoesNotShareListReferences()
    {
      var original = new ChatCompletionRequest
      {
        Model = "ai/x",
        Messages = new List<ChatMessage> { new() { Role = "user", Content = "hi" } },
        Stop = new List<string> { "A" }
      };

      var copy = new ChatCompletionRequest(original);

      // The copy must be a deep copy: mutating the copy's lists must not affect the original.
      Assert.NotSame(original.Messages, copy.Messages);
      Assert.NotSame(original.Stop, copy.Stop);

      copy.Messages.Add(new ChatMessage { Role = "assistant", Content = "yo" });
      copy.Stop.Add("B");

      Assert.Single(original.Messages);
      Assert.Single(original.Stop);
    }

    [Fact]
    public void CompletionRequest_Copy_DoesNotShareListReferences()
    {
      var original = new CompletionRequest { Model = "ai/x", Prompt = "p", Stop = new List<string> { "A" } };

      var copy = new CompletionRequest(original);

      Assert.NotSame(original.Stop, copy.Stop);
      copy.Stop.Add("B");
      Assert.Single(original.Stop);
    }

    // Compares two instances property-by-property over every PUBLIC SETTABLE property using a
    // per-property JSON round-trip for value equality. If a future property is added but the
    // copy mechanism forgets it, the copy's value differs from the source and this fails —
    // guarding NEW6 against regression without hand-listing the property set.
    private static void AssertAllSettablePropertiesEqualByJson<T>(T expected, T actual)
    {
      foreach (var prop in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance))
      {
        if (!prop.CanRead || !prop.CanWrite)
          continue;

        var expectedJson = JsonHelper.Serialize(prop.GetValue(expected));
        var actualJson = JsonHelper.Serialize(prop.GetValue(actual));
        Assert.Equal(expectedJson, actualJson);
      }
    }

    [Fact]
    public async Task EngineInPathToggle_OmitsEngineSegment()
    {
      var conn = new MockModelApiConnection().SetupPost("/chat/completions", 200, DmrFixtures.Load("chat.json"));
      var driver = new OpenAiModelInferenceDriver(conn, ModelRunnerEndpoint.HostTcp().WithEngineInPath(false));

      await driver.ChatCompletionAsync(Ctx, new ChatCompletionRequest { Model = "ai/x" }, TestContext.Current.CancellationToken);

      var request = conn.GetRequests().Single(r => r.Method == "POST");
      Assert.Contains("/engines/v1/chat/completions", request.Path);
      Assert.DoesNotContain("llama.cpp", request.Path);
    }

    // ---- BUG-1: a transport-level failure (ModelRunnerException MIN_003) must NOT be
    // downgraded to RequestFailed (MIN_001) by the driver's broad catch. ----

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData("chat")]
    [InlineData("completion")]
    [InlineData("embeddings")]
    [InlineData("list")]
    public async Task TransportFailure_PreservesEndpointUnreachable_NotRequestFailed(string op)
    {
      // The real ModelApiConnection throws ModelRunnerException(EndpointUnreachable) on a
      // refused/socket transport failure. The driver must surface MIN_003, not MIN_001.
      var driver = new OpenAiModelInferenceDriver(new TransportFailingConnection(), ModelRunnerEndpoint.HostTcp());
      var ct = TestContext.Current.CancellationToken;

      var errorCode = op switch
      {
        "chat" => (await driver.ChatCompletionAsync(Ctx, new ChatCompletionRequest { Model = "ai/x" }, ct)).ErrorCode,
        "completion" => (await driver.CompletionAsync(Ctx, new CompletionRequest { Model = "ai/x", Prompt = "p" }, ct)).ErrorCode,
        "embeddings" => (await driver.EmbeddingsAsync(Ctx, new EmbeddingsRequest { Model = "ai/x", Input = new List<string> { "hi" } }, ct)).ErrorCode,
        "list" => (await driver.ListEngineModelsAsync(Ctx, ct)).ErrorCode,
        _ => null
      };

      Assert.Equal(ErrorCodes.ModelInference.EndpointUnreachable, errorCode);
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData(404, ErrorCodes.ModelInference.RequestFailed)]
    [InlineData(401, ErrorCodes.ModelInference.Unauthorized)]
    [InlineData(500, ErrorCodes.ModelInference.RequestFailed)]
    public async Task ListEngineModelsAsync_HttpError_MapsToTypedCode(int status, string expectedCode)
    {
      // Per-op status-code mapping must hold for the list endpoint exactly as it does for chat.
      var conn = new MockModelApiConnection().SetupGet("/models", status, "boom");
      var driver = Create(conn);

      var resp = await driver.ListEngineModelsAsync(Ctx, TestContext.Current.CancellationToken);

      Assert.False(resp.Success);
      Assert.Equal(expectedCode, resp.ErrorCode);
    }

    // ---- BUG-2/TEST-7: EmbeddingsAsync must copy the request so post-call mutation of the
    // caller's Input cannot alter the already-sent wire body. ----

    [Fact]
    [Trait("Category", "Unit")]
    public async Task EmbeddingsAsync_CopiesRequest_PostCallMutationDoesNotAffectSentBody()
    {
      var conn = new MockModelApiConnection().SetupPost("/embeddings", 200, DmrFixtures.Load("embeddings.json"));
      var driver = Create(conn);

      var input = new List<string> { "original-input" };
      var request = new EmbeddingsRequest { Model = "ai/x", Input = input };

      await driver.EmbeddingsAsync(Ctx, request, TestContext.Current.CancellationToken);

      // Mutate the caller's list AFTER the await: the already-serialized wire body is fixed.
      input.Add("MUTATED");
      input[0] = "CHANGED";

      var sent = conn.GetRequests().Single(r => r.Method == "POST");
      Assert.Contains("original-input", sent.Body);
      Assert.DoesNotContain("MUTATED", sent.Body);
      Assert.DoesNotContain("CHANGED", sent.Body);
    }

    /// <summary>
    /// An <see cref="IModelApiConnection"/> whose every request fails at the transport layer
    /// exactly as the real connection does on a refused/socket error: by throwing
    /// <see cref="ModelRunnerException"/> carrying <c>EndpointUnreachable</c> (MIN_003).
    /// </summary>
    private sealed class TransportFailingConnection : IModelApiConnection
    {
      public Uri BaseAddress => new("http://localhost:12434");
      public TimeSpan? StreamFirstByteTimeout => null;
      public TimeSpan? StreamReadIdleTimeout => null;

      private static ModelRunnerException Boom() =>
          new("Connection refused", ErrorCodes.ModelInference.EndpointUnreachable, new ErrorContext("transport"));

      public Task<HttpResponseMessage> GetAsync(string path, CancellationToken ct = default) => throw Boom();
      public Task<HttpResponseMessage> PostAsync(string path, HttpContent content, CancellationToken ct = default) => throw Boom();
      public Task<HttpResponseMessage> DeleteAsync(string path, CancellationToken ct = default) => throw Boom();
      public Task<Stream> PostStreamAsync(string path, HttpContent content, CancellationToken ct = default) => throw Boom();
      public Task<bool> PingAsync(CancellationToken ct = default) => Task.FromResult(false);
      public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
  }
}
