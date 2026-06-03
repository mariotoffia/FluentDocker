using System;
using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluentDocker.Common
{
  /// <summary>
  /// A <see cref="JsonConverter{T}"/> for <see cref="string"/> that tolerates JSON
  /// values which are not string tokens. Docker and Podman <c>inspect</c> output is
  /// inconsistent: several fields that FluentDocker models as <see cref="string"/>
  /// (for example <c>NetworkSettings.LinkLocalIPv6PrefixLen</c>, <c>GlobalIPv6PrefixLen</c>
  /// and <c>IPPrefixLen</c>) are emitted by the Docker engine as JSON <em>numbers</em>.
  /// </summary>
  /// <remarks>
  /// System.Text.Json throws when a JSON number (or boolean) is deserialized into a
  /// <see cref="string"/> property. Newtonsoft.Json (used in FluentDocker v2) silently
  /// coerced these, so the v3 switch to System.Text.Json regressed inspect parsing for
  /// those containers. This converter restores the lenient behavior by reading the raw
  /// literal text of <c>Number</c>, <c>True</c> and <c>False</c> tokens into the string,
  /// preserving the exact representation (e.g. <c>0</c> stays <c>"0"</c>). Genuine string
  /// and null tokens are passed through unchanged.
  /// </remarks>
  public sealed class TolerantStringConverter : JsonConverter<string>
  {
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
      switch (reader.TokenType)
      {
        case JsonTokenType.String:
          return reader.GetString();
        case JsonTokenType.Null:
          return null;
        case JsonTokenType.Number:
        case JsonTokenType.True:
        case JsonTokenType.False:
          // Read the raw literal bytes of the token and decode as UTF-8 so the
          // exact representation is preserved (e.g. "0", "16", "true").
          var bytes = reader.HasValueSequence
              ? reader.ValueSequence.ToArray()
              : reader.ValueSpan.ToArray();
          return Encoding.UTF8.GetString(bytes);
        default:
          throw new JsonException(
              $"Cannot convert JSON token '{reader.TokenType}' to System.String.");
      }
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
    {
      if (value is null)
        writer.WriteNullValue();
      else
        writer.WriteStringValue(value);
    }
  }
}
