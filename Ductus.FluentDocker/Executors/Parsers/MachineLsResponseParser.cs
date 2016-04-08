using System.Collections.Generic;
using Ductus.FluentDocker.Model;

namespace Ductus.FluentDocker.Executors.Parsers
{
  public class MachineLsResponseParser : IProcessResponseParser<IList<string /*machine-name*/>>
  {
    public CommandResponse<IList<string>> Response { get; private set; }

    public IProcessResponse<IList<string>> Process(ProcessExecutionResult response)
    {
      var list = new List<string>();
      var rows = response.StdOutAsArry;
      if (rows.Length > 2)
      {
        for (var i = 1; i < rows.Length; i++)
        {
          list.Add(rows[i].Substring(0, rows[i].IndexOf(' ')));
        }
      }

      Response = response.ToResponse(true, string.Empty, (IList<string>) list);
      return this;
    }
  }
}