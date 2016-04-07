using System.Collections.Generic;

namespace Ductus.FluentDocker.Executors.Parsers
{
  public sealed class MachineEnvResponseParser : IProcessResponseParser<IDictionary<string, string>>
  {
    public IDictionary<string, string> Response { get; private set; }

    public IProcessResponse<IDictionary<string, string>> Process(string response)
    {
      var dict = new Dictionary<string, string>();
      if (string.IsNullOrEmpty(response))
      {
        Response = dict;
        return this;
      }

      var lines = response.Split('\n');

      foreach (var line in lines)
      {
        if (line.StartsWith("export") || line.StartsWith("SET "))
        {
          var idx = line.StartsWith("SET ") ? 4 : 8;
          var nv = line.Substring(idx, line.Length - idx).Split('=');
          var key = nv[0].Trim();
          var value = nv[1].Trim();

          dict.Add(key, 4 == idx ? value : value.Substring(1, value.Length - 2));
        }
      }

      Response = dict;
      return this;
    }
  }
}