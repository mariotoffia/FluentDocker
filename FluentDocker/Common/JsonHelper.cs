#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace FluentDocker.Common
{
  /// <summary>
  /// Shared System.Text.Json configuration for FluentDocker.
  /// Provides thread-safe, reusable serializer options that match
  /// Docker/Podman JSON conventions (camelCase, targeted lenient parsing).
  /// </summary>
  /// <remarks>
  /// Targeted lenient parsing means <see cref="DateTimeOffset"/> properties tolerate
  /// unparseable daemon-emitted values: an invalid date resolves to
  /// <c>default(DateTimeOffset)</c> instead of failing the whole deserialization. Callers
  /// that pass their own types through <see cref="TryDeserialize{T}(string, out T)"/> inherit
  /// this behavior, so a defaulted <see cref="DateTimeOffset"/> may indicate a malformed value
  /// rather than an absent one.
  /// </remarks>
  public static class JsonHelper
  {
    private static readonly JsonConverter<DateTimeOffset> TolerantDateTimeOffsetConverterInstance = new TolerantDateTimeOffsetConverter();
    private static readonly JsonConverter<DateTimeOffset?> TolerantNullableDateTimeOffsetConverterInstance = new TolerantNullableDateTimeOffsetConverter();
    private static readonly JsonConverter<bool> LenientBoolConverterInstance = new LenientBoolConverter();
    private static readonly JsonConverter<int> LenientInt32ConverterInstance = new LenientInt32Converter();
    private static readonly JsonConverter<long> LenientInt64ConverterInstance = new LenientInt64Converter();

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
    /// <exception cref="System.Text.Json.JsonException">
    /// Thrown when <paramref name="json"/> is invalid JSON.
    /// </exception>
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
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
          return null;

        return root.TryGetProperty(propertyName, out var prop)
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
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
          return null;

        if (!root.TryGetProperty(propertyName, out var prop))
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
        PropertyNameCaseInsensitive = false,
        TypeInfoResolver = CreateTypeInfoResolver()
      };
      options.Converters.Add(new JsonStringEnumConverter());
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
        PropertyNameCaseInsensitive = true,
        TypeInfoResolver = CreateTypeInfoResolver()
      };
      options.Converters.Add(new JsonStringEnumConverter());
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
        PropertyNameCaseInsensitive = true,
        TypeInfoResolver = CreateTypeInfoResolver()
      };
      options.Converters.Add(new JsonStringEnumConverter());
      options.MakeReadOnly(true);
      return options;
    }

    private static IJsonTypeInfoResolver CreateTypeInfoResolver()
    {
      // The former ContainerNetworkSettings prefix-length string modifier is gone: those
      // properties are now lenient ints with [JsonConverter(typeof(LenientInt32Converter))]
      // directly on the DTO (one shape for prefix lengths across the inspect surface).
      var resolver = new DefaultJsonTypeInfoResolver();
      resolver.Modifiers.Add(ApplyTolerantDateTimeOffsetConverters);
      resolver.Modifiers.Add(ApplyLenientBoolConverters);
      resolver.Modifiers.Add(ApplyLenientIntConverters);
      return resolver;
    }

    /// <summary>
    /// Applies <see cref="LenientBoolConverter"/> to every non-nullable <see cref="bool"/>
    /// property of FluentDocker's own model DTOs, so one drifting daemon-emitted boolean
    /// (JSON null, any number, a string form, or even a structured object/array) degrades to a
    /// parsed value instead of failing the entire inspect deserialization. Nullable booleans are left untouched: they already
    /// tolerate JSON null, and null must remain observable as "not set" for options DTOs.
    /// Scoped like the <see cref="DateTimeOffset"/> modifier — user types deserialized through
    /// the shared options are not rewritten (MC-MAJ-1).
    /// </summary>
    private static void ApplyLenientBoolConverters(JsonTypeInfo typeInfo)
    {
      if (typeInfo.Type.Namespace?.StartsWith("FluentDocker.Model", StringComparison.Ordinal) != true)
        return;

      foreach (var property in typeInfo.Properties)
      {
        if (property.PropertyType == typeof(bool))
          property.CustomConverter = LenientBoolConverterInstance;
      }
    }

    /// <summary>
    /// Applies <see cref="LenientInt32Converter"/> to every non-nullable <see cref="int"/> and
    /// <see cref="LenientInt64Converter"/> to every non-nullable <see cref="long"/> property of
    /// FluentDocker's own model DTOs, so one drifting daemon-emitted number (JSON null, a
    /// fractional value, a string form, or even a structured object/array) degrades to a parsed
    /// value instead of failing the entire inspect deserialization. Nullable integers are left
    /// untouched: they already tolerate JSON null, and null must remain observable as "not set".
    /// Scoped like the <see cref="DateTimeOffset"/>/<see cref="bool"/> modifiers — user types
    /// deserialized through the shared options are not rewritten (MODEL-2).
    /// </summary>
    private static void ApplyLenientIntConverters(JsonTypeInfo typeInfo)
    {
      if (typeInfo.Type.Namespace?.StartsWith("FluentDocker.Model", StringComparison.Ordinal) != true)
        return;

      foreach (var property in typeInfo.Properties)
      {
        if (property.PropertyType == typeof(int))
          property.CustomConverter = LenientInt32ConverterInstance;
        else if (property.PropertyType == typeof(long))
          property.CustomConverter = LenientInt64ConverterInstance;
      }
    }

    private static void ApplyTolerantDateTimeOffsetConverters(JsonTypeInfo typeInfo)
    {
      // Scope to FluentDocker's own model DTOs: do not silently rewrite DateTimeOffset parsing for
      // arbitrary user types deserialized through the shared default options (MC-MAJ-1).
      if (typeInfo.Type.Namespace?.StartsWith("FluentDocker.Model", StringComparison.Ordinal) != true)
        return;

      foreach (var property in typeInfo.Properties)
      {
        if (property.PropertyType == typeof(DateTimeOffset))
          property.CustomConverter = TolerantDateTimeOffsetConverterInstance;
        else if (property.PropertyType == typeof(DateTimeOffset?))
          property.CustomConverter = TolerantNullableDateTimeOffsetConverterInstance;
      }
    }
  }
}
