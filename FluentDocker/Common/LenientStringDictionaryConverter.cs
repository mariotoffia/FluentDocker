#nullable enable
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluentDocker.Common
{
  /// <summary>
  /// Reads string dictionaries from JSON objects or Docker CLI key=value lists.
  /// </summary>
  public sealed class LenientStringDictionaryConverter : JsonConverter<Dictionary<string, string>>
  {
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
        throw new JsonException($"Cannot convert JSON token '{reader.TokenType}' to Dictionary<String,String>.");

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
          throw new JsonException($"Cannot convert JSON token '{reader.TokenType}' to System.String.");
      }
    }

    private static Dictionary<string, string> ReadEmptyArray(ref Utf8JsonReader reader)
    {
      if (!reader.Read())
        throw new JsonException("Unexpected end of JSON array.");
      if (reader.TokenType == JsonTokenType.EndArray)
        return [];
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
