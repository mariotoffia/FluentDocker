#nullable enable
using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace FluentDocker.Common
{
  /// <summary>
  /// Reads runtime DateTimeOffset drift as default instead of poisoning the inspect graph.
  /// </summary>
  public sealed class TolerantDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
  {
    private static long _driftCount;

    /// <summary>
    /// Total number of present-but-unparseable <see cref="DateTimeOffset"/> values read as
    /// <c>default</c> (year 0001) since process start. A non-zero, growing value indicates daemon/CLI
    /// timestamp-format drift — otherwise indistinguishable from genuinely-unset dates.
    /// </summary>
    public static long DriftCount => Interlocked.Read(ref _driftCount);

    /// <summary>
    /// Optional diagnostic hook invoked with the raw unparseable token each time drift is observed
    /// (the raw string, or the token-type name for a structured value). Set once at startup.
    /// </summary>
    public static Action<string?>? OnDrift { get; set; }

    /// <summary>
    /// Reads a <see cref="DateTimeOffset"/> from a string token; any other or unparseable value is
    /// treated as drift and read as <c>default</c> instead of throwing (see <see cref="DriftCount"/>).
    /// </summary>
    /// <param name="reader">The reader positioned at the token to convert.</param>
    /// <param name="typeToConvert">The type being converted (always <see cref="DateTimeOffset"/>).</param>
    /// <param name="options">The serializer options in effect.</param>
    /// <returns>The parsed value, or <c>default</c> (year 0001) when the token is present but not a parseable date.</returns>
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
      var raw = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
      if (raw != null &&
          DateTimeOffset.TryParse(
              raw,
              CultureInfo.InvariantCulture,
              DateTimeStyles.AssumeUniversal,
              out var value))
        return value;

      // A present-but-unparseable value is runtime/CLI drift, not a legitimately-absent date (Null).
      // Surface it (counter + optional hook) so wait/uptime logic computing on a zeroed timestamp is
      // diagnosable instead of silent (MC-MAJ-1).
      if (reader.TokenType != JsonTokenType.Null)
      {
        Interlocked.Increment(ref _driftCount);
        OnDrift?.Invoke(raw ?? reader.TokenType.ToString());
      }

      // Drift to an object/array token: consume it so the reader stays aligned; otherwise the
      // unconsumed container corrupts the stream and poisons the whole object (the very failure
      // this converter exists to prevent). Scalars need no skip.
      if (reader.TokenType == JsonTokenType.StartObject || reader.TokenType == JsonTokenType.StartArray)
        reader.Skip();

      return default;
    }

    /// <summary>Writes the value as an ISO-8601 JSON string.</summary>
    /// <param name="writer">The writer to write to.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="options">The serializer options in effect.</param>
    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
    {
      writer.WriteStringValue(value);
    }
  }

  /// <summary>
  /// Nullable counterpart of <see cref="TolerantDateTimeOffsetConverter"/>. A JSON <c>null</c> reads
  /// as <c>null</c>; a present-but-unparseable value drifts to the non-nullable default (and is counted
  /// via <see cref="TolerantDateTimeOffsetConverter.DriftCount"/>) rather than poisoning the payload.
  /// Needed because <c>DateTimeOffset?</c> is the natural type for timestamps Docker omits, and STJ
  /// would otherwise apply strict parsing to it (MC-MAJ-1 / MDL-MAJ-4).
  /// </summary>
  public sealed class TolerantNullableDateTimeOffsetConverter : JsonConverter<DateTimeOffset?>
  {
    private static readonly TolerantDateTimeOffsetConverter Inner = new();

    /// <summary>
    /// Reads a nullable <see cref="DateTimeOffset"/>: JSON <c>null</c> reads as <c>null</c>; any other
    /// token delegates to <see cref="TolerantDateTimeOffsetConverter.Read"/>.
    /// </summary>
    /// <param name="reader">The reader positioned at the token to convert.</param>
    /// <param name="typeToConvert">The type being converted (always nullable <see cref="DateTimeOffset"/>).</param>
    /// <param name="options">The serializer options in effect.</param>
    /// <returns>The parsed value; <c>null</c> for JSON null, or <c>default</c> when present but unparseable.</returns>
    public override DateTimeOffset? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
      if (reader.TokenType == JsonTokenType.Null)
        return null;
      return Inner.Read(ref reader, typeof(DateTimeOffset), options);
    }

    /// <summary>Writes the value as an ISO-8601 JSON string, or JSON null when unset.</summary>
    /// <param name="writer">The writer to write to.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="options">The serializer options in effect.</param>
    public override void Write(Utf8JsonWriter writer, DateTimeOffset? value, JsonSerializerOptions options)
    {
      if (value.HasValue)
        writer.WriteStringValue(value.Value);
      else
        writer.WriteNullValue();
    }
  }
}
