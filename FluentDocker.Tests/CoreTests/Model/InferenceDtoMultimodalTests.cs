using System;
using System.Collections.Generic;
using System.Text.Json;
using FluentDocker.Common;
using FluentDocker.Model.Models.Inference;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Model
{
  public partial class InferenceDtoTests
  {
    [Fact]
    [Trait("Category", "Unit")]
    public void ChatMessage_StringContent_RoundTripsAsStringContent()
    {
      var json = JsonHelper.Serialize(new ChatMessage { Role = "user", Content = "hello" });
      using var doc = JsonDocument.Parse(json);

      Assert.Equal("user", doc.RootElement.GetProperty("role").GetString());
      Assert.Equal(JsonValueKind.String, doc.RootElement.GetProperty("content").ValueKind);
      Assert.Equal("hello", doc.RootElement.GetProperty("content").GetString());

      var message = JsonHelper.TryDeserialize<ChatMessage>("{\"role\":\"user\",\"content\":\"hello\"}");

      Assert.NotNull(message);
      Assert.Equal("hello", message.Content);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ChatMessage_MultimodalContent_RoundTripsWithoutDroppingImageParts()
    {
      const string json =
          "{\"role\":\"user\",\"content\":[" +
          "{\"type\":\"text\",\"text\":\"hi\"}," +
          "{\"type\":\"image_url\",\"image_url\":{\"url\":\"data:image/png;base64,abc\"}}]}";

      var message = JsonHelper.TryDeserialize<ChatMessage>(json);

      Assert.NotNull(message);
      Assert.Equal("hi", message.Content);

      var serialized = JsonHelper.Serialize(message);
      using var doc = JsonDocument.Parse(serialized);
      var content = doc.RootElement.GetProperty("content");

      Assert.Equal(JsonValueKind.Array, content.ValueKind);
      Assert.Equal(2, content.GetArrayLength());
      AssertContainsImageUrl(content, "data:image/png;base64,abc");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ChatMessage_RawContent_SerializesMultimodalArray()
    {
      var message = new ChatMessage
      {
        Role = "user",
        RawContent = JsonSerializer.SerializeToElement(new object[]
        {
          new { type = "text", text = "hi" },
          new { type = "image_url", image_url = new { url = "data:image/png;base64,abc" } }
        })
      };

      var serialized = JsonHelper.Serialize(message);
      using var doc = JsonDocument.Parse(serialized);
      var content = doc.RootElement.GetProperty("content");

      Assert.Equal(JsonValueKind.Array, content.ValueKind);
      Assert.Equal("hi", message.Content);
      AssertContainsImageUrl(content, "data:image/png;base64,abc");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ChatMessage_NullContent_OmitsContent()
    {
      var json = JsonHelper.Serialize(new ChatMessage { Role = "user", Content = null });
      using var doc = JsonDocument.Parse(json);

      Assert.False(doc.RootElement.TryGetProperty("content", out _));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ChatCompletionRequest_CopyCtor_ClonesMultimodalRawContent()
    {
      var original = new ChatCompletionRequest
      {
        Model = "ai/vision",
        Messages = new List<ChatMessage>
        {
          new()
          {
            Role = "user",
            RawContent = JsonSerializer.SerializeToElement(new object[]
            {
              new { type = "text", text = "hi" },
              new { type = "image_url", image_url = new { url = "data:image/png;base64,abc" } }
            })
          }
        }
      };

      var copy = new ChatCompletionRequest(original);

      original.Messages[0].Content = "changed";
      var copiedMessages = copy.Messages ?? throw new InvalidOperationException("Copied messages must be present.");

      Assert.Equal("hi", copiedMessages[0].Content);

      var serialized = JsonHelper.Serialize(copy);
      using var doc = JsonDocument.Parse(serialized);
      var content = doc.RootElement.GetProperty("messages")[0].GetProperty("content");

      Assert.Equal(JsonValueKind.Array, content.ValueKind);
      AssertContainsImageUrl(content, "data:image/png;base64,abc");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ChatMessage_CopyCtor_RejectsAdditionalPropertiesContentCollision()
    {
      var message = new ChatMessage
      {
        Role = "user",
        AdditionalProperties = new Dictionary<string, JsonElement>
        {
          ["content"] = JsonSerializer.SerializeToElement("smuggled")
        }
      };

      var ex = Assert.Throws<ArgumentException>(() => new ChatMessage(message));

      Assert.Contains("content", ex.Message, StringComparison.Ordinal);
    }

    private static void AssertContainsImageUrl(JsonElement content, string expectedUrl)
    {
      foreach (var part in content.EnumerateArray())
      {
        if (part.ValueKind != JsonValueKind.Object)
          continue;
        if (!part.TryGetProperty("type", out var type) || type.GetString() != "image_url")
          continue;
        if (!part.TryGetProperty("image_url", out var imageUrl))
          continue;
        if (imageUrl.TryGetProperty("url", out var url) && url.GetString() == expectedUrl)
          return;
      }

      throw new Xunit.Sdk.XunitException($"Expected image_url part '{expectedUrl}'.");
    }
  }
}
