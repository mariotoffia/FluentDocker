#nullable enable
using System;
using System.Globalization;
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
    /// Deserializes a JSON string to the specified type using case-insensitive options.
    /// Returns <c>default</c> on JSON or unsupported-type failures instead of throwing.
    /// Prefer <see cref="TryDeserialize{T}(string, out T)"/> for value types so a parsed
    /// <c>default</c> value can be distinguished from a failed parse.
    /// </summary>
    public static T? TryDeserialize<T>(string json)
    {
      return TryDeserialize<T>(json, out var value) ? value : default;
    }

    /// <summary>
    /// Deserializes a JSON string to the specified type using case-insensitive options.
    /// Returns <c>true</c> when JSON was valid (including JSON <c>null</c>), otherwise <c>false</c>.
    /// </summary>
    public static bool TryDeserialize<T>(string json, out T? value)
    {
      return TryDeserialize<T>(json, out value, out _);
    }

    /// <summary>
    /// Deserializes a JSON string to the specified type using case-insensitive options,
    /// returning the parse error when deserialization fails.
    /// </summary>
    public static bool TryDeserialize<T>(string json, out T? value, out Exception? error)
    {
      value = default;
      error = null;
      if (string.IsNullOrWhiteSpace(json))
        return false;

      try
      {
        value = JsonSerializer.Deserialize<T>(json, CaseInsensitiveOptions);
        return true;
      }
      // ponytail: covers converter/setter format+overflow escapes; keeps TryDeserialize's never-throw contract.
      catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException or InvalidOperationException or FormatException or OverflowException)
      {
        error = ex;
        return false;
      }
    }

    /// <summary>
    /// Deserializes a JSON string using a source-generated <see cref="JsonTypeInfo{T}"/>.
    /// Returns <c>default</c> on JSON or unsupported-type failures instead of throwing.
    /// </summary>
    public static T? TryDeserialize<T>(string json, JsonTypeInfo<T> typeInfo)
    {
      if (string.IsNullOrWhiteSpace(json))
        return default;

      try
      {
        return JsonSerializer.Deserialize(json, typeInfo);
      }
      // ponytail: covers converter/setter format+overflow escapes; keeps TryDeserialize's never-throw contract.
      catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException or InvalidOperationException or FormatException or OverflowException)
      {
        return default;
      }
    }

    /// <summary>
    /// Deserializes a UTF-8 byte span to the specified type using case-insensitive options.
    /// Returns <c>default</c> on JSON or unsupported-type failures instead of throwing.
    /// </summary>
    public static T? TryDeserialize<T>(ReadOnlySpan<byte> utf8Json)
    {
      if (utf8Json.IsEmpty)
        return default;

      try
      {
        return JsonSerializer.Deserialize<T>(utf8Json, CaseInsensitiveOptions);
      }
      // ponytail: covers converter/setter format+overflow escapes; keeps TryDeserialize's never-throw contract.
      catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException or InvalidOperationException or FormatException or OverflowException)
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
    public static string? TryGetProperty(string json, string propertyName)
    {
      if (string.IsNullOrWhiteSpace(json))
        return null;

      try
      {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty(propertyName, out var prop)
            ? prop.ValueKind == JsonValueKind.String ? prop.GetString() : null
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
        if (!doc.RootElement.TryGetProperty(propertyName, out var prop))
          return null;
        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var value))
          return value;
        if (prop.ValueKind == JsonValueKind.String &&
            int.TryParse(prop.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
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
      options.MakeReadOnly(true);
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
      options.MakeReadOnly(true);
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
      options.MakeReadOnly(true);
      return options;
    }
  }
}
