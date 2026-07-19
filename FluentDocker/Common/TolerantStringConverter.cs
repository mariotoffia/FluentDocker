#nullable enable
using System;
using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluentDocker.Common
{
  /// <summary>
  /// A <see cref="JsonConverter{T}"/> for <see cref="string"/> properties that tolerate JSON
  /// values which are not string tokens — Docker and Podman <c>inspect</c> output emits some
  /// nominally-string fields as JSON <em>numbers</em> or booleans depending on engine version.
  /// </summary>
  /// <remarks>
  /// System.Text.Json throws when a JSON number (or boolean) is deserialized into a
  /// <see cref="string"/> property; Newtonsoft.Json (used in FluentDocker v2) silently
  /// coerced these. Apply this converter via <c>[JsonConverter]</c> to string properties
  /// that must survive that drift. (The network prefix-length fields that originally
  /// motivated it are now lenient <see cref="int"/>s via <see cref="LenientInt32Converter"/>.)
  /// The converter reads the raw literal text of <c>Number</c>, <c>True</c> and <c>False</c>
  /// tokens into the string, preserving the exact representation (e.g. <c>0</c> stays
  /// <c>"0"</c>); structured tokens (object/array) are skipped whole and read as
  /// <c>null</c>. Genuine string and null tokens are passed through unchanged.
  /// </remarks>
  public sealed class TolerantStringConverter : JsonConverter<string?>
  {
    /// <summary>
    /// Reads a string from a string/null token unchanged, or from a number/boolean token by decoding its
    /// raw literal text (see <see cref="TolerantStringConverter"/>).
    /// </summary>
    /// <param name="reader">The reader positioned at the token to convert.</param>
    /// <param name="typeToConvert">The type being converted.</param>
    /// <param name="options">The serializer options in effect.</param>
    /// <returns>The string value; <c>null</c> for a JSON null or a skipped structured token.</returns>
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
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
          return reader.HasValueSequence
              ? Encoding.UTF8.GetString(reader.ValueSequence.ToArray())
              : Encoding.UTF8.GetString(reader.ValueSpan);
        default:
          // Structured drift (object/array) degrades to null instead of poisoning the whole
          // inspect — same skip-and-default policy as the sibling tolerant converters.
          reader.Skip();
          return null;
      }
    }

    /// <summary>Writes the value as a JSON string, or JSON null when <paramref name="value"/> is <c>null</c>.</summary>
    /// <param name="writer">The writer to write to.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="options">The serializer options in effect.</param>
    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
      if (value is null)
        writer.WriteNullValue();
      else
        writer.WriteStringValue(value);
    }
  }
}
