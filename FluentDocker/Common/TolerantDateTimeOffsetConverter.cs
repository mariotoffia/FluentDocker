#nullable enable
using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluentDocker.Common
{
  /// <summary>
  /// Reads runtime DateTimeOffset drift as default instead of poisoning the inspect graph.
  /// </summary>
  public sealed class TolerantDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
  {
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
      if (reader.TokenType == JsonTokenType.String &&
          DateTimeOffset.TryParse(
              reader.GetString(),
              CultureInfo.InvariantCulture,
              DateTimeStyles.AssumeUniversal,
              out var value))
        return value;

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
