using System.Collections.Generic;

namespace Ductus.FluentDocker.Executors.Parsers
{
  public class MachineLsResponseParser : IProcessResponseParser<IList<string/*machine-name*/>>
  {
    public IList<string> Response { get; private set; }
    public IProcessResponse<IList<string>> Process(string response)
    {
      var list = new List<string>();

      var rows = response.Split('\n');
      if (rows.Length > 2)
      {
        for (int i = 1; i < rows.Length;i++)
        {
          if (string.IsNullOrEmpty(rows[i]))
          {
            break;
          }

          list.Add(rows[i].Substring(0,rows[i].IndexOf(' ')));
        }
      }

      Response = list;
      return this;
    }
  }
}