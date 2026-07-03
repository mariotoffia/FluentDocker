using System.Collections.Generic;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace FluentDocker.Model.Builders.FileBuilder
{
  internal static class DockerfileJson
  {
    private static readonly JsonSerializerOptions Options = new()
    {
      Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    internal static string Quote(string value) => JsonSerializer.Serialize(value, Options);

    internal static string Array(IEnumerable<string> values)
    {
      return $"[{string.Join(", ", values.Select(Quote))}]";
    }
  }
}
