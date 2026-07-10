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

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
    {
      writer.WriteStringValue(value);
    }
  }
}
