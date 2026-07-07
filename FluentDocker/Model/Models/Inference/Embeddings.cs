#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluentDocker.Model.Models.Inference
{
  /// <summary>
  /// An OpenAI-compatible embeddings request. (Preview)
  /// </summary>
  public sealed class EmbeddingsRequest
  {
    /// <summary>Creates an empty request.</summary>
    public EmbeddingsRequest()
    {
    }

    /// <summary>
    /// Creates an independent copy of <paramref name="other"/>. Every property is explicitly
    /// copied; mutable collections are deep-copied so callers cannot observe mutations made
    /// inside the driver.
    /// </summary>
    /// <param name="other">The request to copy.</param>
    public EmbeddingsRequest(EmbeddingsRequest other)
    {
      ArgumentNullException.ThrowIfNull(other);
      Model = other.Model;
      Input = other.Input is null ? null : new List<string>(other.Input);
      AdditionalProperties = InferenceDto.CopyExtensionData<EmbeddingsRequest>(other.AdditionalProperties);
    }

    /// <summary>The model id.</summary>
    [JsonPropertyName("model")] public string? Model { get; set; }

    /// <summary>The inputs to embed.</summary>
    [JsonPropertyName("input")] public IList<string>? Input { get; set; }

    /// <summary>
    /// Pass-through for any OpenAI-compatible request field not modeled above (e.g.
    /// <c>encoding_format</c>, <c>dimensions</c>). Captured verbatim so advanced parameters
    /// round-trip to the engine instead of being dropped. (Preview)
    /// </summary>
    [JsonExtensionData] public IDictionary<string, JsonElement>? AdditionalProperties { get; set; }
  }

  /// <summary>
  /// An OpenAI-compatible embeddings response. (Preview)
  /// </summary>
  public sealed class EmbeddingsResponse
  {
    /// <summary>The object type (<c>list</c>).</summary>
    [JsonPropertyName("object")] public string? Object { get; set; }

    /// <summary>The embedding vectors.</summary>
    [JsonPropertyName("data")] public IList<EmbeddingData>? Data { get; set; }

    /// <summary>The model that produced the embeddings.</summary>
    [JsonPropertyName("model")] public string? Model { get; set; }

    /// <summary>Token usage.</summary>
    [JsonPropertyName("usage")] public Usage? Usage { get; set; }

    /// <summary>
    /// Pass-through for any unmodeled response field. Captured verbatim so it is observable
    /// instead of dropped. (Preview)
    /// </summary>
    [JsonExtensionData] public IDictionary<string, JsonElement>? AdditionalProperties { get; set; }
  }

  /// <summary>
  /// A single embedding vector. (Preview)
  /// </summary>
  public sealed class EmbeddingData
  {
    /// <summary>The vector index.</summary>
    [JsonPropertyName("index")] public int Index { get; set; }

    /// <summary>The embedding vector.</summary>
    [JsonPropertyName("embedding")] public IList<float>? Embedding { get; set; }

    /// <summary>
    /// Pass-through for any unmodeled field. Captured verbatim so it is observable
    /// instead of dropped. (Preview)
    /// </summary>
    [JsonExtensionData] public IDictionary<string, JsonElement>? AdditionalProperties { get; set; }
  }
}
