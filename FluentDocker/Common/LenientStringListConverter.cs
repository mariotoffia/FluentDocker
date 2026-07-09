#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluentDocker.Common
{
  /// <summary>
  /// Reads a string list from either a JSON array or Docker CLI's compact string form.
  /// The legacy comma-delimited string form cannot represent items that contain commas.
  /// </summary>
  public sealed class LenientStringListConverter : JsonConverter<List<string>>
  {
    public override List<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
      if (reader.TokenType == JsonTokenType.StartArray)
        return ReadArray(ref reader);

      if (reader.TokenType == JsonTokenType.Null)
        return [];

      if (reader.TokenType != JsonTokenType.String)
        throw new JsonException($"Cannot convert JSON token '{reader.TokenType}' to List<String>.");

      var value = reader.GetString();
      if (string.IsNullOrWhiteSpace(value))
        return [];

      return [.. value.Split(", ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)];
    }

    private static List<string> ReadArray(ref Utf8JsonReader reader)
    {
      var result = new List<string>();
      while (reader.Read())
      {
        if (reader.TokenType == JsonTokenType.EndArray)
          return result;

        result.Add(reader.TokenType switch
        {
          JsonTokenType.String => reader.GetString() ?? string.Empty,
          JsonTokenType.Null => string.Empty,
          _ => throw new JsonException($"Cannot convert JSON token '{reader.TokenType}' to System.String.")
        });
      }

      throw new JsonException("Unexpected end of JSON array.");
    }

    public override void Write(Utf8JsonWriter writer, List<string> value, JsonSerializerOptions options)
    {
      writer.WriteStartArray();
      foreach (var item in value)
        writer.WriteStringValue(item);
      writer.WriteEndArray();
    }
  }
}
