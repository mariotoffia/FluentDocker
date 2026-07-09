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

  public sealed class ChatMessageRawContentConverter : JsonConverter<JsonElement?>
  {
    public override JsonElement? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
      using var doc = JsonDocument.ParseValue(ref reader);
      return doc.RootElement.Clone();
    }

    public override void Write(Utf8JsonWriter writer, JsonElement? value, JsonSerializerOptions options)
    {
      if (value is null)
      {
        writer.WriteNullValue();
        return;
      }

      value.Value.WriteTo(writer);
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
  /// A single chat message (system / user / assistant / tool). Content is a convenience
  /// text projection; set <see cref="RawContent"/> to send multimodal or non-text parts verbatim. (Preview)
  /// </summary>
  public sealed class ChatMessage
  {
    /// <summary>Creates an empty message.</summary>
    public ChatMessage()
    {
    }

    /// <summary>
    /// Creates an independent copy of <paramref name="other"/>. The raw content element and
    /// extension data are cloned so a copied <see cref="ChatMessage"/> survives later source
    /// mutations and source <see cref="JsonDocument"/> disposal.
    /// </summary>
    /// <param name="other">The message to copy.</param>
    public ChatMessage(ChatMessage other)
    {
      ArgumentNullException.ThrowIfNull(other);
      Role = other.Role;
      RawContent = other.RawContent is { } el ? el.Clone() : null;
      Name = other.Name;
      AdditionalProperties = InferenceDto.CopyExtensionData<ChatMessage>(other.AdditionalProperties);
    }

    /// <summary>The role: <c>system</c> | <c>user</c> | <c>assistant</c> | <c>tool</c>.</summary>
    [JsonPropertyName("role")] public string? Role { get; set; }

    /// <summary>
    /// The serialized <c>content</c> value, preserved verbatim for string, array, and object content.
    /// </summary>
    /// <remarks>
    /// Set this to a <see cref="JsonElement"/> array/object to send multimodal or otherwise
    /// non-text content parts. Use <see cref="Content"/> when a plain text message is enough.
    /// Explicit JSON <c>null</c> and an absent <c>content</c> field both normalize to an omitted
    /// <c>content</c> on re-serialization (every OpenAI-compatible server treats them equivalently).
    /// </remarks>
    [JsonPropertyName("content")]
    [JsonConverter(typeof(ChatMessageRawContentConverter))]
    public JsonElement? RawContent { get; set; }

    /// <summary>
    /// Convenience text projection over <see cref="RawContent"/>.
    /// </summary>
    /// <remarks>
    /// String content returns unchanged. Multimodal array content returns concatenated
    /// <c>{"type":"text","text":"..."}</c> parts only; image and other non-text parts remain
    /// preserved in <see cref="RawContent"/> and are re-serialized verbatim. Set
    /// <see cref="RawContent"/> directly to send multimodal / non-text content parts.
    /// </remarks>
    [JsonIgnore]
    public string? Content
    {
      get
      {
        if (RawContent is not { } content)
          return null;
        return content.ValueKind switch
        {
          JsonValueKind.String => content.GetString(),
          JsonValueKind.Array => FlattenTextContent(content),
          _ => null
        };
      }
      set => RawContent = value is null ? null : JsonSerializer.SerializeToElement(value);
    }

    /// <summary>An optional participant name.</summary>
    [JsonPropertyName("name")] public string? Name { get; set; }

    /// <summary>
    /// Pass-through for any unmodeled message field — notably <c>tool_calls</c> on an assistant
    /// message and <c>tool_call_id</c> on a tool message. Modeled fields such as
    /// <c>content</c> are reserved: use <see cref="Content"/> for plain text or
    /// <see cref="RawContent"/> for multimodal/non-text content. (Preview)
    /// </summary>
    [JsonExtensionData] public IDictionary<string, JsonElement>? AdditionalProperties { get; set; }

    private static string FlattenTextContent(JsonElement content)
    {
      var text = new StringBuilder();
      foreach (var part in content.EnumerateArray())
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
