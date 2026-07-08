#nullable enable
using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FluentDocker.Model.Models
{
  /// <summary>
  /// Serializes a <see cref="ModelReference"/> as its canonical string form and
  /// parses it back, so references appear as scalars in DMR request/response JSON.
  /// </summary>
  internal sealed class ModelReferenceJsonConverter : JsonConverter<ModelReference>
  {
    public override ModelReference? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
      if (reader.TokenType == JsonTokenType.Null)
        return null;
      if (reader.TokenType != JsonTokenType.String)
        throw new JsonException($"Cannot convert JSON token '{reader.TokenType}' to ModelReference.");

      var value = reader.GetString();
      if (string.IsNullOrWhiteSpace(value))
        return null;

      if (!ModelReference.TryParse(value, out var model))
        throw new JsonException($"Invalid model reference '{value}'.");

      return model;
    }

    public override void Write(Utf8JsonWriter writer, ModelReference value, JsonSerializerOptions options)
    {
      if (value is null)
        writer.WriteNullValue();
      else
        writer.WriteStringValue(value.ToString());
    }
  }
}
