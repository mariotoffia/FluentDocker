#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluentDocker.Model.Models.Inference
{
  // NOTE: The inference DTOs are a PREVIEW surface — shapes may change before the
  // model subsystem reaches 1.0. They are STJ-annotated and (de)serialized via
  // FluentDocker.Common.JsonHelper. They are reflection-serialized today (no STJ
  // source-gen wired), so NativeAOT/trimming is blocked until a future pass uses
  // JsonHelper's JsonTypeInfo overloads.

  /// <summary>
  /// Shared helpers for the inference DTO copy-constructors.
  /// </summary>
  internal static class InferenceDto
  {
    private static readonly ConcurrentDictionary<Type, HashSet<string>> ReservedNames = new();

    /// <summary>
    /// Copies a DTO's <c>[JsonExtensionData]</c> bag for a copy-constructor. Each
    /// <see cref="JsonElement"/> is CLONED so the copy survives disposal of the source
    /// <see cref="JsonDocument"/> the caller may have built it from, and any key that collides
    /// with a modeled <c>[JsonPropertyName]</c> field of <typeparamref name="T"/> is REJECTED —
    /// such a key would otherwise serialize as a DUPLICATE top-level property and let the
    /// pass-through value silently override the typed one (e.g. an extension <c>"stream"</c>
    /// overriding <see cref="ChatCompletionRequest.Stream"/>).
    /// </summary>
    /// <typeparam name="T">The DTO type whose modeled JSON names are reserved.</typeparam>
    /// <param name="source">The source extension bag (may be null).</param>
    /// <returns>An independent, cloned copy, or <c>null</c> when <paramref name="source"/> is null.</returns>
    /// <exception cref="ArgumentException">A key collides with a modeled field of <typeparamref name="T"/>.</exception>
    public static IDictionary<string, JsonElement>? CopyExtensionData<T>(IDictionary<string, JsonElement>? source)
    {
      if (source is null)
        return null;

      var reserved = ReservedNames.GetOrAdd(typeof(T), ReflectReservedNames);
      var copy = new Dictionary<string, JsonElement>(source.Count, StringComparer.Ordinal);
      foreach (var pair in source)
      {
        if (reserved.Contains(pair.Key))
          throw new ArgumentException(
              $"AdditionalProperties key '{pair.Key}' collides with the modeled '{pair.Key}' field of " +
              $"{typeof(T).Name}; set the typed property instead of passing it through AdditionalProperties.",
              nameof(source));
        copy[pair.Key] = pair.Value.Clone();
      }

      return copy;
    }

    private static HashSet<string> ReflectReservedNames(Type type)
    {
      var names = new HashSet<string>(StringComparer.Ordinal);
      foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
      {
        if (property.IsDefined(typeof(JsonExtensionDataAttribute), inherit: true))
          continue;
        var name = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name;
        if (!string.IsNullOrEmpty(name))
          names.Add(name);
      }

      return names;
    }
  }

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

    /// <summary>
    /// Pass-through for any unmodeled usage field (e.g. <c>prompt_tokens_details</c>). Captured
    /// verbatim so it is observable instead of dropped. (Preview)
    /// </summary>
    [JsonExtensionData] public IDictionary<string, JsonElement>? AdditionalProperties { get; set; }
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
      AdditionalProperties = InferenceDto.CopyExtensionData<ChatMessage>(other.AdditionalProperties);
    }

    /// <summary>The role: <c>system</c> | <c>user</c> | <c>assistant</c> | <c>tool</c>.</summary>
    [JsonPropertyName("role")] public string? Role { get; set; }

    /// <summary>The message content.</summary>
    [JsonPropertyName("content")] public string? Content { get; set; }

    /// <summary>An optional participant name.</summary>
    [JsonPropertyName("name")] public string? Name { get; set; }

    /// <summary>
    /// Pass-through for any unmodeled message field — notably <c>tool_calls</c> on an assistant
    /// message and <c>tool_call_id</c> on a tool message, plus multimodal content. Captured
    /// verbatim so it round-trips instead of being dropped. (Preview)
    /// </summary>
    [JsonExtensionData] public IDictionary<string, JsonElement>? AdditionalProperties { get; set; }
  }

  /// <summary>
  /// A model as listed by the engine's OpenAI <c>/models</c> endpoint (distinct
  /// from the local OCI store). (Preview)
  /// </summary>
  public sealed class OpenAiModel
  {
    /// <summary>The model id.</summary>
    [JsonPropertyName("id")] public string? Id { get; set; }

    /// <summary>The object type (<c>model</c>).</summary>
    [JsonPropertyName("object")] public string? Object { get; set; }

    /// <summary>The owner.</summary>
    [JsonPropertyName("owned_by")] public string? OwnedBy { get; set; }

    /// <summary>The creation timestamp (unix seconds).</summary>
    [JsonPropertyName("created")] public long Created { get; set; }

    /// <summary>
    /// Pass-through for any unmodeled field. Captured verbatim so it is observable
    /// instead of dropped. (Preview)
    /// </summary>
    [JsonExtensionData] public IDictionary<string, JsonElement>? AdditionalProperties { get; set; }
  }

  /// <summary>
  /// The envelope returned by the engine's OpenAI <c>/models</c> listing. (Preview)
  /// </summary>
  public sealed class OpenAiModelList
  {
    /// <summary>The object type (<c>list</c>).</summary>
    [JsonPropertyName("object")] public string? Object { get; set; }

    /// <summary>The listed models.</summary>
    [JsonPropertyName("data")] public IList<OpenAiModel>? Data { get; set; }

    /// <summary>
    /// Pass-through for any unmodeled field. Captured verbatim so it is observable
    /// instead of dropped. (Preview)
    /// </summary>
    [JsonExtensionData] public IDictionary<string, JsonElement>? AdditionalProperties { get; set; }
  }
}
