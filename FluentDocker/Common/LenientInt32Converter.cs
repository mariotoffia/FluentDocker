#nullable enable
using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluentDocker.Common
{
  /// <summary>
  /// Reads Int32 values from numbers or numeric strings, using 0 for null or unparsable runtime drift.
  /// </summary>
  public sealed class LenientInt32Converter : JsonConverter<int>
  {
    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
      if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var number))
        return number;

      if (reader.TokenType == JsonTokenType.Number)
        return 0;

      if (reader.TokenType == JsonTokenType.String &&
          int.TryParse(reader.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
        return number;

      if (reader.TokenType == JsonTokenType.String)
        return 0;

      if (reader.TokenType == JsonTokenType.Null)
        return 0;

      throw new JsonException($"Cannot convert JSON token '{reader.TokenType}' to Int32.");
    }

    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
    {
      writer.WriteNumberValue(value);
    }
  }
}
