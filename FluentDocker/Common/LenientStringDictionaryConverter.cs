#nullable enable
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace FluentDocker.Common
{
  /// <summary>
  /// Reads string dictionaries from JSON objects or Docker CLI key=value lists.
  /// </summary>
  public sealed class LenientStringDictionaryConverter : JsonConverter<Dictionary<string, string>>
  {
    private static long _driftCount;

    /// <summary>
    /// Total number of structurally-drifted tokens observed (an unexpected top-level token, a
    /// non-empty JSON array, or a nested object/array where a scalar value was expected) since
    /// process start. Mirrors <see cref="TolerantDateTimeOffsetConverter.DriftCount"/>; JSON null
    /// (a legitimate 'unset') and successful parses are not counted.
    /// </summary>
    public static long DriftCount => Interlocked.Read(ref _driftCount);

    /// <summary>
    /// Reads a string dictionary from a JSON object, an empty JSON array (Docker sometimes emits <c>[]</c>
    /// instead of <c>{}</c> for an empty map), or a compact <c>key=value,key=value</c> string.
    /// </summary>
    /// <param name="reader">The reader positioned at the token to convert.</param>
    /// <param name="typeToConvert">The type being converted.</param>
    /// <param name="options">The serializer options in effect.</param>
    /// <returns>The parsed dictionary; empty for a JSON null, an empty array, or an empty/blank string.</returns>
    /// <exception cref="JsonException">The token is not an object, array, or string, or an object entry is malformed.</exception>
    public override Dictionary<string, string> Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
      if (reader.TokenType == JsonTokenType.StartObject)
        return ReadObject(ref reader);

      if (reader.TokenType == JsonTokenType.Null)
        return [];

      if (reader.TokenType == JsonTokenType.StartArray)
        return ReadEmptyArray(ref reader);

      if (reader.TokenType != JsonTokenType.String)
      {
        // Structured drift: a token that is neither object, null, empty-array, nor string. Surface
        // it before failing so the format drift is observable via DriftCount.
        Interlocked.Increment(ref _driftCount);
        throw new JsonException($"Cannot convert JSON token '{reader.TokenType}' to Dictionary<String,String>.");
      }

      var value = reader.GetString();
      if (string.IsNullOrWhiteSpace(value))
        return [];

      return ReadCompactString(value);
    }

    private static Dictionary<string, string> ReadObject(ref Utf8JsonReader reader)
    {
      var result = new Dictionary<string, string>();
      while (reader.Read())
      {
        if (reader.TokenType == JsonTokenType.EndObject)
          return result;

        if (reader.TokenType != JsonTokenType.PropertyName)
          throw new JsonException($"Expected property name but found '{reader.TokenType}'.");

        var key = reader.GetString();
        if (string.IsNullOrEmpty(key) || !reader.Read())
          throw new JsonException("Invalid dictionary entry.");

        result[key] = ReadStringValue(ref reader);
      }

      throw new JsonException("Unexpected end of JSON object.");
    }

    private static string ReadStringValue(ref Utf8JsonReader reader)
    {
      switch (reader.TokenType)
      {
        case JsonTokenType.String:
          return reader.GetString() ?? string.Empty;
        case JsonTokenType.Null:
          return string.Empty;
        case JsonTokenType.Number:
        case JsonTokenType.True:
        case JsonTokenType.False:
          var bytes = reader.HasValueSequence
              ? reader.ValueSequence.ToArray()
              : reader.ValueSpan.ToArray();
          return Encoding.UTF8.GetString(bytes);
        default:
          // Structured drift (a nested object/array where a scalar was expected): skip and degrade
          // to an empty string instead of throwing, matching the sibling lenient converters so one
          // bad label/option value never aborts the whole deserialization (MODEL-1). Count it so the
          // format drift is observable via DriftCount.
          if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
          {
            Interlocked.Increment(ref _driftCount);
            reader.Skip();
          }
          return string.Empty;
      }
    }

    private static Dictionary<string, string> ReadEmptyArray(ref Utf8JsonReader reader)
    {
      if (!reader.Read())
        throw new JsonException("Unexpected end of JSON array.");
      if (reader.TokenType == JsonTokenType.EndArray)
        return [];
      // A non-empty array is structured drift; count it before failing so the drift is observable.
      Interlocked.Increment(ref _driftCount);
      throw new JsonException("Only an empty JSON array can be converted to Dictionary<String,String>.");
    }

    private static Dictionary<string, string> ReadCompactString(string value)
    {
      var result = new Dictionary<string, string>();
      foreach (var pair in SplitCompactPairs(value))
      {
        var index = pair.IndexOf('=');
        if (index <= 0)
          continue;

        var key = pair[..index].Trim();
        if (key.Length > 0)
        {
          var val = pair[(index + 1)..].Trim();
          // ponytail: drop a dangling trailing separator ("a=1," -> "1"), matching old RemoveEmptyEntries semantics
          if (val.EndsWith(','))
            val = val[..^1].TrimEnd();
          result[key] = val;
        }
      }

      return result;
    }

    private static IEnumerable<string> SplitCompactPairs(string value)
    {
      var start = 0;
      for (var i = 0; i < value.Length; i++)
      {
        if (value[i] != ',' || !StartsKeyValue(value, i + 1))
          continue;

        yield return value[start..i].Trim();
        start = i + 1;
      }

      yield return value[start..].Trim();
    }

    private static bool StartsKeyValue(string value, int start)
    {
      while (start < value.Length && char.IsWhiteSpace(value[start]))
        start++;

      var equals = value.IndexOf('=', start);
      if (equals <= start)
        return false;

      var comma = value.IndexOf(',', start);
      return comma < 0 || equals < comma;
    }

    /// <summary>Writes the dictionary as a JSON object of string properties.</summary>
    /// <param name="writer">The writer to write to.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="options">The serializer options in effect.</param>
    public override void Write(
        Utf8JsonWriter writer, Dictionary<string, string> value, JsonSerializerOptions options)
    {
      writer.WriteStartObject();
      foreach (var pair in value)
        writer.WriteString(pair.Key, pair.Value);
      writer.WriteEndObject();
    }
  }
}
