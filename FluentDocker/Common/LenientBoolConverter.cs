#nullable enable
using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluentDocker.Common
{
  /// <summary>
  /// Reads booleans emitted as JSON booleans, numeric 0/1, string "true"/"false", or string "0"/"1".
  /// </summary>
  /// <remarks>Docker and Podman sometimes emit missing boolean values as JSON null; null is treated as false.</remarks>
  public sealed class LenientBoolConverter : JsonConverter<bool>
  {
    public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
      if (reader.TokenType is JsonTokenType.True or JsonTokenType.False)
        return reader.GetBoolean();

      if (reader.TokenType == JsonTokenType.Null)
        return false;

      if (reader.TokenType == JsonTokenType.Number &&
          reader.TryGetInt32(out var number) &&
          number is 0 or 1)
        return number == 1;

      if (reader.TokenType == JsonTokenType.String)
      {
        var text = reader.GetString()?.Trim();
        if (bool.TryParse(text, out var value))
          return value;
        if (text is "0" or "1")
          return text == "1";
      }

      throw new JsonException($"Cannot convert JSON token '{reader.TokenType}' to Boolean.");
    }

    public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options)
    {
      writer.WriteBooleanValue(value);
    }
  }
}
