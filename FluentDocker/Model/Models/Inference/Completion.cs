using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluentDocker.Model.Models.Inference
{
  /// <summary>
  /// An OpenAI-compatible text completion request. (Preview)
  /// </summary>
  public sealed class CompletionRequest
  {
    /// <summary>Creates an empty request.</summary>
    public CompletionRequest()
    {
    }

    /// <summary>
    /// Creates an independent copy of <paramref name="other"/>. Used by the driver to set
    /// <see cref="Stream"/> without mutating the caller's request instance. Every property
    /// is explicitly copied; mutable collections are deep-copied so callers cannot observe
    /// mutations made inside the driver.
    /// </summary>
    /// <param name="other">The request to copy.</param>
    public CompletionRequest(CompletionRequest other)
    {
      ArgumentNullException.ThrowIfNull(other);
      Model = other.Model;
      Prompt = other.Prompt;
      MaxTokens = other.MaxTokens;
      Temperature = other.Temperature;
      TopP = other.TopP;
      Stream = other.Stream;
      Stop = other.Stop is null ? null : new List<string>(other.Stop);
      Seed = other.Seed;
      AdditionalProperties = InferenceDto.CopyExtensionData<CompletionRequest>(other.AdditionalProperties);
    }

    /// <summary>The model id.</summary>
    [JsonPropertyName("model")] public string Model { get; set; }

    /// <summary>The prompt text.</summary>
    [JsonPropertyName("prompt")] public string Prompt { get; set; }

    /// <summary>The maximum number of tokens to generate.</summary>
    [JsonPropertyName("max_tokens")] public int? MaxTokens { get; set; }

    /// <summary>Sampling temperature.</summary>
    [JsonPropertyName("temperature")] public double? Temperature { get; set; }

    /// <summary>Nucleus sampling probability.</summary>
    [JsonPropertyName("top_p")] public double? TopP { get; set; }

    /// <summary>Whether to stream the response as SSE.</summary>
    [JsonPropertyName("stream")] public bool? Stream { get; set; }

    /// <summary>Stop sequences.</summary>
    [JsonPropertyName("stop")] public IList<string> Stop { get; set; }

    /// <summary>Sampling seed.</summary>
    [JsonPropertyName("seed")] public int? Seed { get; set; }

    /// <summary>
    /// Pass-through for any OpenAI-compatible request field not modeled above (e.g.
    /// <c>response_format</c>, <c>logit_bias</c>). Captured verbatim so advanced parameters
    /// round-trip to the engine instead of being dropped. (Preview)
    /// </summary>
    [JsonExtensionData] public IDictionary<string, JsonElement> AdditionalProperties { get; set; }
  }

  /// <summary>
  /// An OpenAI-compatible text completion response. (Preview)
  /// </summary>
  public sealed class CompletionResponse
  {
    /// <summary>The response id.</summary>
    [JsonPropertyName("id")] public string Id { get; set; }

    /// <summary>The object type (<c>text_completion</c>).</summary>
    [JsonPropertyName("object")] public string Object { get; set; }

    /// <summary>Creation timestamp (unix seconds).</summary>
    [JsonPropertyName("created")] public long Created { get; set; }

    /// <summary>The model that produced the response.</summary>
    [JsonPropertyName("model")] public string Model { get; set; }

    /// <summary>The choices.</summary>
    [JsonPropertyName("choices")] public IList<CompletionChoice> Choices { get; set; }

    /// <summary>Token usage.</summary>
    [JsonPropertyName("usage")] public Usage Usage { get; set; }

    /// <summary>
    /// Pass-through for any unmodeled response field. Captured verbatim so it is observable
    /// instead of dropped. (Preview)
    /// </summary>
    [JsonExtensionData] public IDictionary<string, JsonElement> AdditionalProperties { get; set; }
  }

  /// <summary>
  /// A single text completion choice. (Preview)
  /// </summary>
  public sealed class CompletionChoice
  {
    /// <summary>The choice index.</summary>
    [JsonPropertyName("index")] public int Index { get; set; }

    /// <summary>The generated text.</summary>
    [JsonPropertyName("text")] public string Text { get; set; }

    /// <summary>The finish reason.</summary>
    [JsonPropertyName("finish_reason")] public string FinishReason { get; set; }

    /// <summary>
    /// Pass-through for any unmodeled choice field (e.g. <c>logprobs</c>). Captured verbatim
    /// so it is observable instead of dropped. (Preview)
    /// </summary>
    [JsonExtensionData] public IDictionary<string, JsonElement> AdditionalProperties { get; set; }
  }

  /// <summary>
  /// A streaming text completion chunk (one SSE <c>data:</c> line). (Preview)
  /// </summary>
  public sealed class CompletionChunk
  {
    /// <summary>The response id.</summary>
    [JsonPropertyName("id")] public string Id { get; set; }

    /// <summary>The model.</summary>
    [JsonPropertyName("model")] public string Model { get; set; }

    /// <summary>The choices.</summary>
    [JsonPropertyName("choices")] public IList<CompletionChoice> Choices { get; set; }

    /// <summary>
    /// Pass-through for any unmodeled chunk field. Captured verbatim so it is observable
    /// instead of dropped. (Preview)
    /// </summary>
    [JsonExtensionData] public IDictionary<string, JsonElement> AdditionalProperties { get; set; }
  }
}
