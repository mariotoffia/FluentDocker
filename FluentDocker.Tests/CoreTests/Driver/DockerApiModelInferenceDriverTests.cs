using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
