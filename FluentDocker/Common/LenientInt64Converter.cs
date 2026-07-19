#nullable enable
using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace FluentDocker.Common
{
  /// <summary>
  /// Reads Int64 values from JSON numbers or numeric strings.
  /// </summary>
  /// <remarks>
  /// The <see cref="long"/> counterpart of <see cref="LenientInt32Converter"/>. Drift-tolerant:
  /// a JSON null, an unparsable string, or a structurally different token (object/array — skipped
  /// whole) reads as <c>0</c> instead of failing the entire deserialization. One bad
  /// daemon-emitted field (e.g. a fractional or null exit code) must never kill a whole inspect.
  /// </remarks>
  public sealed class LenientInt64Converter : JsonConverter<long>
  {
    private static long _driftCount;

    /// <summary>
    /// Total number of present-but-unparseable <see cref="long"/> tokens read as <c>0</c> since
    /// process start. A non-zero, growing value indicates daemon/CLI numeric-format drift —
    /// otherwise indistinguishable from a genuinely-zero field. Mirrors
    /// <see cref="TolerantDateTimeOffsetConverter.DriftCount"/>; JSON null (a legitimate 'unset')
    /// and successful parses are not counted.
    /// </summary>
    public static long DriftCount => Interlocked.Read(ref _driftCount);

    /// <summary>Reads a <see cref="long"/> per the lenient rules described on <see cref="LenientInt64Converter"/>.</summary>
    /// <param name="reader">The reader positioned at the token to convert.</param>
    /// <param name="typeToConvert">The type being converted (always <see cref="long"/>).</param>
    /// <param name="options">The serializer options in effect.</param>
    /// <returns>
    /// The parsed value, saturated to <see cref="long.MinValue"/>/<see cref="long.MaxValue"/> when
    /// out of range; <c>0</c> for null or any unparsable/drifting token.
    /// </returns>
    public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
      if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt64(out var number))
        return number;

      if (reader.TokenType == JsonTokenType.Number && reader.TryGetDouble(out var doubleNumber))
        return ToInt64Saturated(doubleNumber);

      if (reader.TokenType == JsonTokenType.String &&
          long.TryParse(reader.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
        return number;

      if (reader.TokenType == JsonTokenType.String &&
          double.TryParse(reader.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out doubleNumber))
        return ToInt64Saturated(doubleNumber);

      // Drift tolerance: null, an unparsable string, or a structurally different token
      // degrades to 0 rather than poisoning the whole deserialization. Structured tokens
      // are skipped in full so the reader stays positioned correctly. A present-but-unparseable
      // value is drift (counted); JSON null is a legitimate 'unset' and is not.
      if (reader.TokenType != JsonTokenType.Null)
        Interlocked.Increment(ref _driftCount);
      if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
        reader.Skip();
      return 0;
    }

    /// <summary>Writes the value as a JSON number.</summary>
    /// <param name="writer">The writer to write to.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="options">The serializer options in effect.</param>
    public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options)
    {
      writer.WriteNumberValue(value);
    }

    private static long ToInt64Saturated(double value)
    {
      if (double.IsNaN(value))
        return 0;
      if (value >= long.MaxValue)
        return long.MaxValue;
      if (value <= long.MinValue)
        return long.MinValue;
      return (long)value;
    }
  }
}
