using System.Collections.Generic;
using FluentDocker.Common;
using FluentDocker.Model.Models.Inference;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Model
{
  /// <summary>
  /// (De)serialization tests for the OpenAI-compatible inference DTOs, using
  /// real payloads captured from Docker Model Runner v1.2.1.
  /// </summary>
  [Trait("Category", "Unit")]
  public class InferenceDtoTests
  {
    // Real non-stream chat response captured from ai/smollm2.
    private const string ChatJson =
        "{\"choices\":[{\"finish_reason\":\"length\",\"index\":0,\"message\":{\"role\":\"assistant\"," +
        "\"content\":\"Hello! My name is SmolLM\"}}],\"created\":1782191248,\"model\":\"model.gguf\"," +
        "\"system_fingerprint\":\"b1-ac4cdde\",\"object\":\"chat.completion\",\"usage\":{\"completion_tokens\":8," +
        "\"prompt_tokens\":36,\"total_tokens\":44,\"prompt_tokens_details\":{\"cached_tokens\":0}}," +
        "\"id\":\"chatcmpl-abc\"}";

    // Real SSE chunk payload (the JSON after the "data: " prefix).
    private const string ChatChunkJson =
        "{\"choices\":[{\"finish_reason\":null,\"index\":0,\"delta\":{\"content\":\"I\"}}]," +
        "\"created\":1782191248,\"id\":\"chatcmpl-x\",\"model\":\"model.gguf\",\"object\":\"chat.completion.chunk\"}";

    private const string CompletionJson =
        "{\"choices\":[{\"text\":\" Paris, and it is located\",\"index\":0,\"logprobs\":null," +
        "\"finish_reason\":\"length\"}],\"created\":1782191248,\"model\":\"model.gguf\"," +
        "\"object\":\"text_completion\",\"usage\":{\"completion_tokens\":5,\"prompt_tokens\":5,\"total_tokens\":10}," +
        "\"id\":\"cmpl-1\"}";

    private const string EmbeddingsJson =
        "{\"model\":\"ai/embeddinggemma\",\"object\":\"list\",\"usage\":{\"prompt_tokens\":4,\"total_tokens\":4}," +
        "\"data\":[{\"embedding\":[0.0489,0.0224,-0.0287]}]}";

    private const string ModelsListJson =
        "{\"object\":\"list\",\"data\":[{\"id\":\"docker.io/ai/smollm2:latest\",\"object\":\"model\"," +
        "\"created\":1742816981,\"owned_by\":\"docker\"}]}";

    [Fact]
    public void ChatCompletionResponse_Deserializes()
    {
      var resp = JsonHelper.TryDeserialize<ChatCompletionResponse>(ChatJson);

      Assert.NotNull(resp);
      Assert.Equal("chatcmpl-abc", resp.Id);
      Assert.Equal("chat.completion", resp.Object);
      Assert.Single(resp.Choices);
      Assert.Equal("assistant", resp.Choices[0].Message.Role);
      Assert.Equal("Hello! My name is SmolLM", resp.Choices[0].Message.Content);
      Assert.Equal("length", resp.Choices[0].FinishReason);
      Assert.Equal(44, resp.Usage.TotalTokens);
      Assert.Equal(36, resp.Usage.PromptTokens);
    }

    [Fact]
    public void ChatCompletionRequest_SerializesSnakeCaseAndOmitsNulls()
    {
      var req = new ChatCompletionRequest
      {
        Model = "ai/smollm2",
        Messages = new List<ChatMessage> { new() { Role = "user", Content = "hi" } },
        MaxTokens = 20,
        Stream = false
      };

      var json = JsonHelper.Serialize(req);

      Assert.Contains("\"model\":\"ai/smollm2\"", json);
      Assert.Contains("\"messages\"", json);
      Assert.Contains("\"max_tokens\":20", json);
      Assert.Contains("\"stream\":false", json);
      // unset nullable props must be omitted (IgnoreNull)
      Assert.DoesNotContain("temperature", json);
      Assert.DoesNotContain("top_p", json);
    }

    [Fact]
    public void ChatCompletionChunk_Deserializes()
    {
      var chunk = JsonHelper.TryDeserialize<ChatCompletionChunk>(ChatChunkJson);

      Assert.NotNull(chunk);
      Assert.Equal("chatcmpl-x", chunk.Id);
      Assert.Single(chunk.Choices);
      Assert.Equal("I", chunk.Choices[0].Delta.Content);
      Assert.Null(chunk.Choices[0].FinishReason);
    }

    [Fact]
    public void CompletionResponse_Deserializes()
    {
      var resp = JsonHelper.TryDeserialize<CompletionResponse>(CompletionJson);

      Assert.NotNull(resp);
      Assert.Equal("text_completion", resp.Object);
      Assert.Single(resp.Choices);
      Assert.Equal(" Paris, and it is located", resp.Choices[0].Text);
      Assert.Equal("length", resp.Choices[0].FinishReason);
      Assert.Equal(10, resp.Usage.TotalTokens);
    }

    [Fact]
    public void EmbeddingsResponse_Deserializes()
    {
      var resp = JsonHelper.TryDeserialize<EmbeddingsResponse>(EmbeddingsJson);

      Assert.NotNull(resp);
      Assert.Equal("ai/embeddinggemma", resp.Model);
      Assert.Single(resp.Data);
      Assert.Equal(3, resp.Data[0].Embedding.Count);
      Assert.Equal(0.0489, resp.Data[0].Embedding[0], 4);
    }

    [Fact]
    public void EmbeddingsRequest_Serializes()
    {
      var req = new EmbeddingsRequest { Model = "ai/embeddinggemma", Input = new List<string> { "hello" } };
      var json = JsonHelper.Serialize(req);

      Assert.Contains("\"model\":\"ai/embeddinggemma\"", json);
      Assert.Contains("\"input\":[\"hello\"]", json);
    }

    [Fact]
    public void OpenAiModelList_Deserializes()
    {
      var list = JsonHelper.TryDeserialize<OpenAiModelList>(ModelsListJson);

      Assert.NotNull(list);
      Assert.Single(list.Data);
      Assert.Equal("docker.io/ai/smollm2:latest", list.Data[0].Id);
      Assert.Equal("model", list.Data[0].Object);
      Assert.Equal("docker", list.Data[0].OwnedBy);
    }
  }
}
