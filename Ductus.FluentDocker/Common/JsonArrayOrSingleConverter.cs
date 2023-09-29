using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Ductus.FluentDocker.Common
{
  public class JsonArrayOrSingleConverter<T> : JsonConverter
  {
    public override bool CanConvert(Type objectType)
    {
      return objectType == typeof(T[]);
    }

    public override object ReadJson(
      JsonReader reader,
      Type objectType,
      object existingValue,
      JsonSerializer serializer)
    {
      var token = JToken.Load(reader);
      if (token.Type == JTokenType.Array)
      {
        return token.ToObject<T[]>();
      }

      return new[] {token.ToObject<T>()};
    }

    public override bool CanWrite => false;

    public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer) =>
      throw new NotImplementedException();
  }
}
