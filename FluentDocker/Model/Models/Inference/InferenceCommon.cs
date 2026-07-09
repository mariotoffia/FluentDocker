#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
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
    /// overriding <see cref="ChatCompletionRequest.Stream"/>). Modeled fields such as
    /// <c>ChatMessage.content</c> must use their typed property; any tolerant read handling
    /// belongs on that property, not in <c>AdditionalProperties</c>.
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

  public sealed class ChatMessageContentConverter : JsonConverter<string?>
  {
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
      if (reader.TokenType == JsonTokenType.Null)
        return null;
      if (reader.TokenType == JsonTokenType.String)
        return reader.GetString();
      if (reader.TokenType != JsonTokenType.StartArray)
        throw new JsonException("Chat message content must be a JSON string or an array of content parts.");

      using var doc = JsonDocument.ParseValue(ref reader);
      var text = new StringBuilder();
      foreach (var part in doc.RootElement.EnumerateArray())
      {
        if (part.ValueKind != JsonValueKind.Object)
          continue;
        if (!part.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String ||
            !string.Equals(type.GetString(), "text", StringComparison.Ordinal))
          continue;
        if (part.TryGetProperty("text", out var value) && value.ValueKind == JsonValueKind.String)
          text.Append(value.GetString());
      }

      return text.ToString();
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
      if (value is null)
      {
        writer.WriteNullValue();
        return;
      }

      writer.WriteStringValue(value);
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

    /// <summary>The message text content.</summary>
    /// <remarks>
    /// Inbound OpenAI multimodal array content is accepted only to concatenate text parts
    /// (<c>{"type":"text","text":"..."}</c>). Image and other non-text parts are not modeled
    /// and are dropped; outbound content is always written as a plain JSON string.
    /// </remarks>
    [JsonPropertyName("content")]
    [JsonConverter(typeof(ChatMessageContentConverter))]
    public string? Content { get; set; }

    /// <summary>An optional participant name.</summary>
    [JsonPropertyName("name")] public string? Name { get; set; }

    /// <summary>
    /// Pass-through for any unmodeled message field — notably <c>tool_calls</c> on an assistant
    /// message and <c>tool_call_id</c> on a tool message. Modeled fields such as
    /// <c>content</c> are reserved: array content is accepted on read by <see cref="Content"/>
    /// and text parts are concatenated, but image/non-text parts are not modeled and outbound
    /// content is string-only. (Preview)
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
