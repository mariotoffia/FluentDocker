#nullable enable
using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluentDocker.Common
{
  /// <summary>
  /// Reads booleans emitted either as JSON booleans or as "true"/"false" strings.
  /// </summary>
  public sealed class LenientBoolConverter : JsonConverter<bool>
  {
    public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
      if (reader.TokenType is JsonTokenType.True or JsonTokenType.False)
        return reader.GetBoolean();

      if (reader.TokenType == JsonTokenType.String &&
          bool.TryParse(reader.GetString(), out var value))
        return value;

      throw new JsonException($"Cannot convert JSON token '{reader.TokenType}' to Boolean.");
    }

    public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options)
    {
      writer.WriteBooleanValue(value);
    }
  }
}
