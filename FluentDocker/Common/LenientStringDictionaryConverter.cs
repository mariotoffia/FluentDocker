using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluentDocker.Common
{
  /// <summary>
  /// Reads string dictionaries from JSON objects or Docker CLI key=value lists.
  /// </summary>
  public sealed class LenientStringDictionaryConverter : JsonConverter<Dictionary<string, string>>
  {
    public override Dictionary<string, string> Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
      if (reader.TokenType == JsonTokenType.StartObject)
        return JsonSerializer.Deserialize<Dictionary<string, string>>(ref reader, options) ?? [];

      if (reader.TokenType == JsonTokenType.Null)
        return [];

      if (reader.TokenType != JsonTokenType.String)
        throw new JsonException($"Cannot convert JSON token '{reader.TokenType}' to Dictionary<String,String>.");

      var result = new Dictionary<string, string>();
      var value = reader.GetString();
      if (string.IsNullOrWhiteSpace(value))
        return result;

      foreach (var pair in value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
      {
        var index = pair.IndexOf('=');
        if (index > 0)
          result[pair[..index]] = pair[(index + 1)..];
      }

      return result;
    }

    public override void Write(
        Utf8JsonWriter writer, Dictionary<string, string> value, JsonSerializerOptions options)
    {
      JsonSerializer.Serialize(writer, value, options);
    }
  }
}
