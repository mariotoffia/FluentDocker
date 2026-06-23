using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FluentDocker.Model.Models.Inference
{
  /// <summary>
  /// An OpenAI-compatible embeddings request. (Preview)
  /// </summary>
  public sealed class EmbeddingsRequest
  {
    /// <summary>The model id.</summary>
    [JsonPropertyName("model")] public string Model { get; set; }

    /// <summary>The inputs to embed.</summary>
    [JsonPropertyName("input")] public IList<string> Input { get; set; }
  }

  /// <summary>
  /// An OpenAI-compatible embeddings response. (Preview)
  /// </summary>
  public sealed class EmbeddingsResponse
  {
    /// <summary>The object type (<c>list</c>).</summary>
    [JsonPropertyName("object")] public string Object { get; set; }

    /// <summary>The embedding vectors.</summary>
    [JsonPropertyName("data")] public IList<EmbeddingData> Data { get; set; }

    /// <summary>The model that produced the embeddings.</summary>
    [JsonPropertyName("model")] public string Model { get; set; }

    /// <summary>Token usage.</summary>
    [JsonPropertyName("usage")] public Usage Usage { get; set; }
  }

  /// <summary>
  /// A single embedding vector. (Preview)
  /// </summary>
  public sealed class EmbeddingData
  {
    /// <summary>The vector index.</summary>
    [JsonPropertyName("index")] public int Index { get; set; }

    /// <summary>The embedding vector.</summary>
    [JsonPropertyName("embedding")] public IList<float> Embedding { get; set; }
  }
}
