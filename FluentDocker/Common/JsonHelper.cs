using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace FluentDocker.Common
{
  /// <summary>
  /// Shared System.Text.Json configuration for FluentDocker.
  /// Provides thread-safe, reusable serializer options that match
  /// Docker/Podman JSON conventions (camelCase, lenient parsing).
  /// </summary>
  public static class JsonHelper
  {
    /// <summary>
    /// Default serializer options matching Docker/Podman JSON conventions.
    /// Thread-safe and reusable.
    /// </summary>
    public static JsonSerializerOptions DefaultOptions { get; } = CreateDefaultOptions();

    /// <summary>
    /// Options for case-insensitive deserialization (handles both PascalCase and camelCase).
    /// </summary>
    public static JsonSerializerOptions CaseInsensitiveOptions { get; } = CreateCaseInsensitiveOptions();

    /// <summary>
    /// Options that produce indented JSON output. Uses default naming/null conventions.
    /// </summary>
    public static JsonSerializerOptions IndentedOptions { get; } = CreateIndentedOptions();

    /// <summary>
    /// Deserializes a JSON string to the specified type using default options.
    /// Returns <c>default</c> on failure instead of throwing.
    /// </summary>
    public static T TryDeserialize<T>(string json)
    {
      if (string.IsNullOrWhiteSpace(json))
        return default;

      try
      {
        return JsonSerializer.Deserialize<T>(json, CaseInsensitiveOptions);
      }
      catch (JsonException)
      {
        return default;
      }
    }

    /// <summary>
    /// Deserializes a JSON string using a source-generated <see cref="JsonTypeInfo{T}"/>.
    /// Returns <c>default</c> on failure instead of throwing.
    /// </summary>
    public static T TryDeserialize<T>(string json, JsonTypeInfo<T> typeInfo)
    {
      if (string.IsNullOrWhiteSpace(json))
        return default;

      try
      {
        return JsonSerializer.Deserialize(json, typeInfo);
      }
      catch (JsonException)
      {
        return default;
      }
    }

    /// <summary>
    /// Deserializes a UTF-8 byte span to the specified type using default options.
    /// Returns <c>default</c> on failure instead of throwing.
    /// </summary>
    public static T TryDeserialize<T>(ReadOnlySpan<byte> utf8Json)
    {
      if (utf8Json.IsEmpty)
        return default;

      try
      {
        return JsonSerializer.Deserialize<T>(utf8Json, CaseInsensitiveOptions);
      }
      catch (JsonException)
      {
        return default;
      }
    }

    /// <summary>
    /// Serializes an object to a JSON string using default options.
    /// </summary>
    public static string Serialize<T>(T value) =>
        JsonSerializer.Serialize(value, DefaultOptions);

    /// <summary>
    /// Serializes an object to UTF-8 bytes using default options.
    /// </summary>
    public static byte[] SerializeToUtf8Bytes<T>(T value) =>
        JsonSerializer.SerializeToUtf8Bytes(value, DefaultOptions);

    /// <summary>
    /// Serializes an object to an indented JSON string using default options.
    /// </summary>
    public static string SerializeIndented<T>(T value) =>
        JsonSerializer.Serialize(value, IndentedOptions);

    /// <summary>
    /// Parses a JSON string and returns a cloned <see cref="JsonElement"/>.
    /// The returned element is detached from the <see cref="JsonDocument"/> and safe to store.
    /// </summary>
    public static JsonElement ParseElement(string json)
    {
      using var doc = JsonDocument.Parse(json);
      return doc.RootElement.Clone();
    }

    /// <summary>
    /// Tries to extract a string property from a JSON string without full deserialization.
    /// Useful for NDJSON streams where only one field is needed.
    /// </summary>
    public static string TryGetProperty(string json, string propertyName)
    {
      if (string.IsNullOrWhiteSpace(json))
        return null;

      try
      {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty(propertyName, out var prop)
            ? prop.GetString()
            : null;
      }
      catch (JsonException)
      {
        return null;
      }
    }

    /// <summary>
    /// Tries to extract an integer property from a JSON string without full deserialization.
    /// </summary>
    public static int? TryGetIntProperty(string json, string propertyName)
    {
      if (string.IsNullOrWhiteSpace(json))
        return null;

      try
      {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty(propertyName, out var prop) &&
            prop.ValueKind == JsonValueKind.Number &&
            prop.TryGetInt32(out var value))
          return value;
        return null;
      }
      catch (JsonException)
      {
        return null;
      }
    }

    private static JsonSerializerOptions CreateDefaultOptions()
    {
      var options = new JsonSerializerOptions
      {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        PropertyNameCaseInsensitive = false
      };
      options.Converters.Add(new JsonStringEnumConverter());
      options.Converters.Add(new TolerantStringConverter());
      return options;
    }

    private static JsonSerializerOptions CreateCaseInsensitiveOptions()
    {
      var options = new JsonSerializerOptions
      {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        PropertyNameCaseInsensitive = true
      };
      options.Converters.Add(new JsonStringEnumConverter());
      options.Converters.Add(new TolerantStringConverter());
      return options;
    }

    private static JsonSerializerOptions CreateIndentedOptions()
    {
      var options = new JsonSerializerOptions
      {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        PropertyNameCaseInsensitive = true
      };
      options.Converters.Add(new JsonStringEnumConverter());
      options.Converters.Add(new TolerantStringConverter());
      return options;
    }
  }
}
