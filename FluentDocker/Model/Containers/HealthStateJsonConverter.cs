#nullable enable
using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluentDocker.Model.Containers
{
  internal sealed class HealthStateJsonConverter : JsonConverter<HealthState>
  {
    public override HealthState Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
      if (reader.TokenType == JsonTokenType.String &&
          Enum.TryParse<HealthState>(reader.GetString(), true, out var value) &&
          Enum.IsDefined(value))
        return value;

      // Drift to a structured token (Status: {...} / [...]) must be consumed so the reader stays
      // aligned for the rest of the container-inspect parse, instead of desyncing and failing the
      // whole document (MC-MAJ-2, mirrors TolerantDateTimeOffsetConverter).
      if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
        reader.Skip();

      return HealthState.Unknown;
    }

    public override void Write(Utf8JsonWriter writer, HealthState value, JsonSerializerOptions options)
    {
      writer.WriteStringValue(value.ToString());
    }
  }
}
