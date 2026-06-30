using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluentDocker.Model.Models.Inference
{
  /// <summary>
  /// A streaming chat completion chunk (one SSE <c>data:</c> line). (Preview)
  /// </summary>
  public sealed class ChatCompletionChunk
  {
    /// <summary>The response id.</summary>
    [JsonPropertyName("id")] public string Id { get; set; }

    /// <summary>The object type (<c>chat.completion.chunk</c>).</summary>
    [JsonPropertyName("object")] public string Object { get; set; }

    /// <summary>The model that produced the chunk.</summary>
    [JsonPropertyName("model")] public string Model { get; set; }

    /// <summary>The delta choices.</summary>
    [JsonPropertyName("choices")] public IList<ChatChunkChoice> Choices { get; set; }

    /// <summary>
    /// Pass-through for any unmodeled chunk field. Captured verbatim so it is observable
    /// instead of dropped. (Preview)
    /// </summary>
    [JsonExtensionData] public IDictionary<string, JsonElement> AdditionalProperties { get; set; }
  }

  /// <summary>
  /// A streaming chat completion choice (a delta). (Preview)
  /// </summary>
  public sealed class ChatChunkChoice
  {
    /// <summary>The choice index.</summary>
    [JsonPropertyName("index")] public int Index { get; set; }

    /// <summary>The incremental message delta.</summary>
    [JsonPropertyName("delta")] public ChatMessage Delta { get; set; }

    /// <summary>The finish reason (null until the final chunk).</summary>
    [JsonPropertyName("finish_reason")] public string FinishReason { get; set; }

    /// <summary>
    /// Pass-through for any unmodeled choice field. Captured verbatim so it is observable
    /// instead of dropped. (Preview)
    /// </summary>
    [JsonExtensionData] public IDictionary<string, JsonElement> AdditionalProperties { get; set; }
  }
}
