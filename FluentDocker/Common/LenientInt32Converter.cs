#nullable enable
using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluentDocker.Common
{
  /// <summary>
  /// Reads Int32 values from JSON numbers or numeric strings.
  /// </summary>
  /// <remarks>Returns <c>0</c> for a JSON null; throws <see cref="JsonException"/> for any other unparsable token.</remarks>
  public sealed class LenientInt32Converter : JsonConverter<int>
  {
    /// <summary>Reads an <see cref="int"/> per the lenient rules described on <see cref="LenientInt32Converter"/>.</summary>
    /// <param name="reader">The reader positioned at the token to convert.</param>
    /// <param name="typeToConvert">The type being converted (always <see cref="int"/>).</param>
    /// <param name="options">The serializer options in effect.</param>
    /// <returns>The parsed value, saturated to <see cref="int.MinValue"/>/<see cref="int.MaxValue"/> when out of range; <c>0</c> for a JSON null.</returns>
    /// <exception cref="JsonException">The token cannot be interpreted as a number.</exception>
    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
      if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var number))
        return number;

      if (reader.TokenType == JsonTokenType.Number && reader.TryGetDouble(out var doubleNumber))
        return ToInt32Saturated(doubleNumber);

      if (reader.TokenType == JsonTokenType.String &&
          int.TryParse(reader.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
        return number;

      if (reader.TokenType == JsonTokenType.String &&
          double.TryParse(reader.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out doubleNumber))
        return ToInt32Saturated(doubleNumber);

      if (reader.TokenType == JsonTokenType.Null)
        return 0;

      throw new JsonException($"Cannot convert JSON token '{reader.TokenType}' to Int32.");
    }

    /// <summary>Writes the value as a JSON number.</summary>
    /// <param name="writer">The writer to write to.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="options">The serializer options in effect.</param>
    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
    {
      writer.WriteNumberValue(value);
    }

    private static int ToInt32Saturated(double value)
    {
      if (double.IsNaN(value))
        return 0;
      if (value >= int.MaxValue)
        return int.MaxValue;
      if (value <= int.MinValue)
        return int.MinValue;
      return (int)value;
    }
  }
}
