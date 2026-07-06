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
        return JsonSerializer.Deserialize<List<string>>(ref reader, options) ?? [];

      if (reader.TokenType == JsonTokenType.Null)
        return [];

      if (reader.TokenType != JsonTokenType.String)
        throw new JsonException($"Cannot convert JSON token '{reader.TokenType}' to List<String>.");

      var value = reader.GetString();
      if (string.IsNullOrWhiteSpace(value))
        return [];

      return [.. value.Split(", ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)];
    }

    public override void Write(Utf8JsonWriter writer, List<string> value, JsonSerializerOptions options)
    {
      JsonSerializer.Serialize(writer, value, options);
    }
  }
}
