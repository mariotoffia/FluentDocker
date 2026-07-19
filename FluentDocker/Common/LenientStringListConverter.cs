#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace FluentDocker.Common
{
  /// <summary>
  /// Reads a string list from either a JSON array or Docker CLI's compact string form.
  /// The compact form splits on commas (with or without a following space — common Docker
  /// versions join <c>docker service ls</c> ports with a bare comma), while commas INSIDE a
  /// port publish spec (range lists like <c>*:80-81,84,86-87-&gt;80</c>) are kept: a comma
  /// starts a new entry only once the accumulated entry already reads as a complete spec
  /// (contains <c>-&gt;</c> or <c>/</c>).
  /// </summary>
  public sealed class LenientStringListConverter : JsonConverter<List<string>>
  {
    private static long _driftCount;

    /// <summary>
    /// Total number of structurally-drifted tokens observed (an unexpected top-level token, or an
    /// array element that is neither a string nor null) since process start. Mirrors
    /// <see cref="TolerantDateTimeOffsetConverter.DriftCount"/>; JSON null (a legitimate 'unset')
    /// and successful parses are not counted.
    /// </summary>
    public static long DriftCount => Interlocked.Read(ref _driftCount);

    /// <summary>
    /// Reads a string list from a JSON array, or from the legacy comma-delimited compact string form
    /// (see <see cref="LenientStringListConverter"/>).
    /// </summary>
    /// <param name="reader">The reader positioned at the token to convert.</param>
    /// <param name="typeToConvert">The type being converted.</param>
    /// <param name="options">The serializer options in effect.</param>
    /// <returns>The parsed list; empty for a JSON null or an empty/blank string.</returns>
    /// <exception cref="JsonException">The token is not an array or string, or an array element is not a string/null.</exception>
    public override List<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
      if (reader.TokenType == JsonTokenType.StartArray)
        return ReadArray(ref reader);

      if (reader.TokenType == JsonTokenType.Null)
        return [];

      if (reader.TokenType != JsonTokenType.String)
      {
        // Structured drift: a token that is neither array, null, nor string. Surface it before
        // failing so the format drift is observable via DriftCount.
        Interlocked.Increment(ref _driftCount);
        throw new JsonException($"Cannot convert JSON token '{reader.TokenType}' to List<String>.");
      }

      var value = reader.GetString();
      if (string.IsNullOrWhiteSpace(value))
        return [];

      // Split on the comma alone — common Docker versions join `docker service ls` ports
      // with a bare comma ("80/tcp,443/tcp") — but re-join segments that CONTINUE the
      // previous entry: port-range publish specs legally contain bare commas
      // ("*:80-81,84,86-87->80"). A comma starts a new entry only once the accumulated
      // entry already reads as a complete spec (contains "->" or "/").
      var result = new List<string>();
      foreach (var raw in value.Split(','))
      {
        var part = raw.Trim();
        if (result.Count > 0 && !LooksLikeCompleteEntry(result[^1]))
        {
          result[^1] = $"{result[^1]},{part}";
          continue;
        }

        if (part.Length > 0)
          result.Add(part);
      }

      return result;
    }

    private static JsonException ReportDriftedElement(JsonTokenType tokenType)
    {
      // A non-string/non-null array element is structured drift; count it before failing so the
      // format drift is observable via DriftCount.
      Interlocked.Increment(ref _driftCount);
      return new JsonException($"Cannot convert JSON token '{tokenType}' to System.String.");
    }

    private static bool LooksLikeCompleteEntry(string entry) =>
        entry.Contains("->", StringComparison.Ordinal) || entry.Contains('/', StringComparison.Ordinal);

    private static List<string> ReadArray(ref Utf8JsonReader reader)
    {
      var result = new List<string>();
      while (reader.Read())
      {
        if (reader.TokenType == JsonTokenType.EndArray)
          return result;

        result.Add(reader.TokenType switch
        {
          JsonTokenType.String => reader.GetString() ?? string.Empty,
          JsonTokenType.Null => string.Empty,
          _ => throw ReportDriftedElement(reader.TokenType)
        });
      }

      throw new JsonException("Unexpected end of JSON array.");
    }

    /// <summary>Writes the list as a JSON array of strings.</summary>
    /// <param name="writer">The writer to write to.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="options">The serializer options in effect.</param>
    public override void Write(Utf8JsonWriter writer, List<string> value, JsonSerializerOptions options)
    {
      writer.WriteStartArray();
      foreach (var item in value)
        writer.WriteStringValue(item);
      writer.WriteEndArray();
    }
  }
}
