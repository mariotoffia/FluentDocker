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
  /// <remarks>
  /// Drift-tolerant: a JSON null, an unparsable string, or a structurally different token
  /// (object/array — skipped whole) reads as <c>0</c> instead of failing the entire
  /// deserialization. One bad daemon-emitted field must never kill a whole inspect.
  /// </remarks>
  public sealed class LenientInt32Converter : JsonConverter<int>
  {
    /// <summary>Reads an <see cref="int"/> per the lenient rules described on <see cref="LenientInt32Converter"/>.</summary>
    /// <param name="reader">The reader positioned at the token to convert.</param>
    /// <param name="typeToConvert">The type being converted (always <see cref="int"/>).</param>
    /// <param name="options">The serializer options in effect.</param>
    /// <returns>
    /// The parsed value, saturated to <see cref="int.MinValue"/>/<see cref="int.MaxValue"/> when
    /// out of range; <c>0</c> for null or any unparsable/drifting token.
    /// </returns>
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

      // Drift tolerance: null, an unparsable string, or a structurally different token
      // degrades to 0 rather than poisoning the whole deserialization. Structured tokens
      // are skipped in full so the reader stays positioned correctly.
      if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
        reader.Skip();
      return 0;
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
