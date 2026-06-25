using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FluentDocker.Model.Models.Inference
{
  // NOTE: The inference DTOs are a PREVIEW surface — shapes may change before the
  // model subsystem reaches 1.0. They are STJ-annotated and (de)serialized via
  // FluentDocker.Common.JsonHelper.

  /// <summary>
  /// Token-usage accounting shared by chat / completion / embeddings responses. (Preview)
  /// </summary>
  public sealed class Usage
  {
    /// <summary>Tokens consumed by the prompt.</summary>
    [JsonPropertyName("prompt_tokens")] public int PromptTokens { get; set; }

    /// <summary>Tokens generated in the completion.</summary>
    [JsonPropertyName("completion_tokens")] public int CompletionTokens { get; set; }

    /// <summary>Total tokens (prompt + completion).</summary>
    [JsonPropertyName("total_tokens")] public int TotalTokens { get; set; }
  }

  /// <summary>
  /// A single chat message (system / user / assistant / tool). (Preview)
  /// </summary>
  public sealed class ChatMessage
  {
    /// <summary>Creates an empty message.</summary>
    public ChatMessage()
    {
    }

    /// <summary>
    /// Creates an independent copy of <paramref name="other"/>. Although all current
    /// properties are strings (immutable), the public setters mean a caller could
    /// mutate the element after construction. This copy constructor ensures that
    /// each <see cref="ChatMessage"/> in a cloned <see cref="ChatCompletionRequest"/>
    /// is a fully independent instance so mutations in the driver copy cannot leak
    /// back to the caller's original list.
    /// </summary>
    /// <param name="other">The message to copy.</param>
    public ChatMessage(ChatMessage other)
    {
      ArgumentNullException.ThrowIfNull(other);
      Role = other.Role;
      Content = other.Content;
      Name = other.Name;
    }

    /// <summary>The role: <c>system</c> | <c>user</c> | <c>assistant</c> | <c>tool</c>.</summary>
    [JsonPropertyName("role")] public string Role { get; set; }

    /// <summary>The message content.</summary>
    [JsonPropertyName("content")] public string Content { get; set; }

    /// <summary>An optional participant name.</summary>
    [JsonPropertyName("name")] public string Name { get; set; }
  }

  /// <summary>
  /// A model as listed by the engine's OpenAI <c>/models</c> endpoint (distinct
  /// from the local OCI store). (Preview)
  /// </summary>
  public sealed class OpenAiModel
  {
    /// <summary>The model id.</summary>
    [JsonPropertyName("id")] public string Id { get; set; }

    /// <summary>The object type (<c>model</c>).</summary>
    [JsonPropertyName("object")] public string Object { get; set; }

    /// <summary>The owner.</summary>
    [JsonPropertyName("owned_by")] public string OwnedBy { get; set; }

    /// <summary>The creation timestamp (unix seconds).</summary>
    [JsonPropertyName("created")] public long Created { get; set; }
  }

  /// <summary>
  /// The envelope returned by the engine's OpenAI <c>/models</c> listing. (Preview)
  /// </summary>
  public sealed class OpenAiModelList
  {
    /// <summary>The object type (<c>list</c>).</summary>
    [JsonPropertyName("object")] public string Object { get; set; }

    /// <summary>The listed models.</summary>
    [JsonPropertyName("data")] public IList<OpenAiModel> Data { get; set; }
  }
}
