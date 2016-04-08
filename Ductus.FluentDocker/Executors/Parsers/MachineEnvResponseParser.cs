using System.Collections.Generic;
using Ductus.FluentDocker.Model;

namespace Ductus.FluentDocker.Executors.Parsers
{
  public sealed class MachineEnvResponseParser : IProcessResponseParser<IDictionary<string, string>>
  {
    public CommandResponse<IDictionary<string, string>> Response { get; private set; }

    public IProcessResponse<IDictionary<string, string>> Process(ProcessExecutionResult response)
    {
      var dict = new Dictionary<string, string>();
      if (string.IsNullOrEmpty(response.StdOut))
      {
        Response = response.ToResponse(false, "No data", (IDictionary<string, string>) dict);
        return this;
      }

      var lines = response.StdOutAsArry;
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

      Response = response.ToResponse(true, string.Empty, (IDictionary<string, string>) dict);
      return this;
    }
  }
}