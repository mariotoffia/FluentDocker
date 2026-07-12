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
    /// <summary>
    /// Reads a string list from a JSON array, or from the legacy comma-delimited compact string form
    /// (see <see cref="LenientStringListConverter"/>).
    /// </summary>
    /// <param name="reader">The reader positioned at the token to convert.</param>
    /// <param name="typeToConvert">The type being converted.</param>
    /// <param name="options">The serializer options in effect.</param>
    /// <returns>The parsed list; empty for a JSON null or an empty/blank string.</returns>
    /// <exception cref="JsonException">The token is not an array or string, or an array element is not a string/null.</exception>
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

    /// <summary>Writes the list as a JSON array of strings.</summary>
    /// <param name="writer">The writer to write to.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="options">The serializer options in effect.</param>
    public override void Write(Utf8JsonWriter writer, List<string> value, JsonSerializerOptions options)
    {
      writer.WriteStartArray();
      foreach (var item in value)
        writer.WriteStringValue(item);
      writer.WriteEndArray();
    }
  }
}
