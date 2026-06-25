using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
  /// Unit tests for <see cref="DockerApiModelInferenceDriver"/> (non-streaming)
  /// driven by <see cref="MockModelApiConnection"/> and real DMR payloads.
  /// </summary>
  [Trait("Category", "Unit")]
  public class DockerApiModelInferenceDriverTests
  {
    private static DriverContext Ctx => new("docker");

    private static DockerApiModelInferenceDriver Create(MockModelApiConnection conn) =>
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
      Assert.Contains("\"stream\":false", sent.Body.Replace(" ", string.Empty));
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
      Assert.Contains("\"stream\":false", sent.Body.Replace(" ", string.Empty));
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

    // ---- A5/M15: a literal JSON `null` body must NOT be treated as a successful null payload ----

    [Fact]
    public async Task ChatCompletionAsync_NullBody_ThrowsParseError()
    {
      var conn = new MockModelApiConnection().SetupPost("/chat/completions", 200, "null");
      var driver = Create(conn);

      var ex = await Assert.ThrowsAsync<ModelRunnerException>(() =>
          driver.ChatCompletionAsync(Ctx, new ChatCompletionRequest { Model = "ai/x" }, TestContext.Current.CancellationToken));

      Assert.Equal(ErrorCodes.ModelInference.StreamParseError, ex.ErrorCode);
    }

    [Fact]
    public async Task CompletionAsync_NullBody_ThrowsParseError()
    {
      var conn = new MockModelApiConnection().SetupPost("/completions", 200, "null");
      var driver = Create(conn);

      var ex = await Assert.ThrowsAsync<ModelRunnerException>(() =>
          driver.CompletionAsync(Ctx, new CompletionRequest { Model = "ai/x", Prompt = "p" }, TestContext.Current.CancellationToken));

      Assert.Equal(ErrorCodes.ModelInference.StreamParseError, ex.ErrorCode);
    }

    [Fact]
    public async Task EmbeddingsAsync_NullBody_ThrowsParseError()
    {
      var conn = new MockModelApiConnection().SetupPost("/embeddings", 200, "null");
      var driver = Create(conn);

      var ex = await Assert.ThrowsAsync<ModelRunnerException>(() =>
          driver.EmbeddingsAsync(Ctx, new EmbeddingsRequest { Model = "ai/x", Input = new List<string> { "hi" } }, TestContext.Current.CancellationToken));

      Assert.Equal(ErrorCodes.ModelInference.StreamParseError, ex.ErrorCode);
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
      var driver = new DockerApiModelInferenceDriver(conn, ModelRunnerEndpoint.HostTcp().WithEngineInPath(false));

      await driver.ChatCompletionAsync(Ctx, new ChatCompletionRequest { Model = "ai/x" }, TestContext.Current.CancellationToken);

      var request = conn.GetRequests().Single(r => r.Method == "POST");
      Assert.Contains("/engines/v1/chat/completions", request.Path);
      Assert.DoesNotContain("llama.cpp", request.Path);
    }
  }
}
