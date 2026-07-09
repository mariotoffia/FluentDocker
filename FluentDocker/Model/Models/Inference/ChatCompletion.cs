#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluentDocker.Model.Models.Inference
{
  /// <summary>
  /// An OpenAI-compatible chat completion request. (Preview)
  /// </summary>
  public sealed class ChatCompletionRequest
  {
    /// <summary>Creates an empty request.</summary>
    public ChatCompletionRequest()
    {
    }

    /// <summary>
    /// Creates an independent copy of <paramref name="other"/>. Used by the driver to set
    /// <see cref="Stream"/> without mutating the caller's request instance. Every property
    /// is explicitly copied; mutable collections are deep-copied so callers cannot observe
    /// mutations made inside the driver.
    /// </summary>
    /// <param name="other">The request to copy.</param>
    public ChatCompletionRequest(ChatCompletionRequest other)
    {
      ArgumentNullException.ThrowIfNull(other);
      Model = other.Model;
      // Deep-copy each ChatMessage element: although current properties are strings
      // (immutable values), the public setters mean a caller can mutate an element
      // post-construction. Cloning each element ensures the driver copy is fully
      // independent of the caller's original list.
      Messages = other.Messages?.Select(m => new ChatMessage(m)).ToList();
      MaxTokens = other.MaxTokens;
      Temperature = other.Temperature;
      TopP = other.TopP;
      Stream = other.Stream;
      Stop = other.Stop is null ? null : new List<string>(other.Stop);
      PresencePenalty = other.PresencePenalty;
      FrequencyPenalty = other.FrequencyPenalty;
      Seed = other.Seed;
      AdditionalProperties = InferenceDto.CopyExtensionData<ChatCompletionRequest>(other.AdditionalProperties);
    }

    /// <summary>The model id (e.g. <c>ai/qwen3</c>).</summary>
    [JsonPropertyName("model")] public string? Model { get; set; }

    /// <summary>The conversation so far.</summary>
    [JsonPropertyName("messages")] public IList<ChatMessage>? Messages { get; set; }

    /// <summary>The maximum number of tokens to generate.</summary>
    [JsonPropertyName("max_tokens")] public int? MaxTokens { get; set; }

    /// <summary>Sampling temperature.</summary>
    [JsonPropertyName("temperature")] public double? Temperature { get; set; }

    /// <summary>Nucleus sampling probability.</summary>
    [JsonPropertyName("top_p")] public double? TopP { get; set; }

    /// <summary>Whether to stream the response as SSE.</summary>
    [JsonPropertyName("stream")] public bool? Stream { get; set; }

    /// <summary>Stop sequences.</summary>
    [JsonPropertyName("stop")] public IList<string>? Stop { get; set; }

    /// <summary>Presence penalty.</summary>
    [JsonPropertyName("presence_penalty")] public double? PresencePenalty { get; set; }

    /// <summary>Frequency penalty.</summary>
    [JsonPropertyName("frequency_penalty")] public double? FrequencyPenalty { get; set; }

    /// <summary>Sampling seed (reproducibility).</summary>
    [JsonPropertyName("seed")] public long? Seed { get; set; }

    /// <summary>
    /// Pass-through for any OpenAI-compatible request field not modeled above (e.g.
    /// <c>tools</c>, <c>tool_choice</c>, <c>response_format</c>). Message <c>content</c> is a
    /// modeled field, not extension data: inbound array content is reduced to text parts and
    /// outbound content remains string-only. (Preview)
    /// </summary>
    [JsonExtensionData] public IDictionary<string, JsonElement>? AdditionalProperties { get; set; }
  }

  /// <summary>
  /// An OpenAI-compatible chat completion response. (Preview)
  /// </summary>
  public sealed class ChatCompletionResponse
  {
    /// <summary>The response id.</summary>
    [JsonPropertyName("id")] public string? Id { get; set; }

    /// <summary>The object type (<c>chat.completion</c>).</summary>
    [JsonPropertyName("object")] public string? Object { get; set; }

    /// <summary>Creation timestamp (unix seconds).</summary>
    [JsonPropertyName("created")] public long Created { get; set; }

    /// <summary>The model that produced the response.</summary>
    [JsonPropertyName("model")] public string? Model { get; set; }

    /// <summary>The choices.</summary>
    [JsonPropertyName("choices")] public IList<ChatChoice>? Choices { get; set; }

    /// <summary>Token usage.</summary>
    [JsonPropertyName("usage")] public Usage? Usage { get; set; }

    /// <summary>
    /// Pass-through for any response field not modeled above. Captured verbatim so unmodeled
    /// server fields (e.g. <c>system_fingerprint</c>) are observable instead of dropped. (Preview)
    /// </summary>
    [JsonExtensionData] public IDictionary<string, JsonElement>? AdditionalProperties { get; set; }
  }

  /// <summary>
  /// A single chat completion choice. (Preview)
  /// </summary>
  public sealed class ChatChoice
  {
    /// <summary>The choice index.</summary>
    [JsonPropertyName("index")] public int Index { get; set; }

    /// <summary>The assistant message.</summary>
    [JsonPropertyName("message")] public ChatMessage? Message { get; set; }

    /// <summary>The finish reason (<c>stop</c> / <c>length</c> / …).</summary>
    [JsonPropertyName("finish_reason")] public string? FinishReason { get; set; }

    /// <summary>
    /// Pass-through for any unmodeled choice field (e.g. <c>logprobs</c>). Captured verbatim
    /// so it is observable instead of dropped. (Preview)
    /// </summary>
    [JsonExtensionData] public IDictionary<string, JsonElement>? AdditionalProperties { get; set; }
  }
}
