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
    /// <summary>Reads a boolean per the lenient rules described on <see cref="LenientBoolConverter"/>.</summary>
    /// <param name="reader">The reader positioned at the token to convert.</param>
    /// <param name="typeToConvert">The type being converted (always <see cref="bool"/>).</param>
    /// <param name="options">The serializer options in effect.</param>
    /// <returns>The parsed value, or <c>false</c> for a JSON null.</returns>
    /// <exception cref="JsonException">The token is not one of the recognized boolean representations.</exception>
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

    /// <summary>Writes the value as a JSON boolean.</summary>
    /// <param name="writer">The writer to write to.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="options">The serializer options in effect.</param>
    public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options)
    {
      writer.WriteBooleanValue(value);
    }
  }
}
