using System;
using System.Collections.Generic;
using System.Linq;
using Ductus.FluentDocker.Model.Containers;
using Ductus.FluentDocker.Model.Machines;
using Ductus.FluentDocker.Services;

namespace Ductus.FluentDocker.Executors.Parsers
{
  public class MachineLsResponseParser : IProcessResponseParser<IList<MachineLsResponse /*machine-name*/>>
  {
    public CommandResponse<IList<MachineLsResponse>> Response { get; private set; }

    public IProcessResponse<IList<MachineLsResponse>> Process(ProcessExecutionResult response)
    {
      var list = new List<MachineLsResponse>();
      var rows = response.StdOutAsArry;

      if (rows.Length > 0)
      {
        list.AddRange(from row in rows
                      select row.Split(';')
          into s
                      where s.Length == 3
                      select new MachineLsResponse
                      {
                        State = s[1] == "Running" ? ServiceRunningState.Running : ServiceRunningState.Stopped,
                        Name = s[0],
                        Docker = string.IsNullOrWhiteSpace(s[2]) ? null : new Uri(s[2])
                      });
      }

      Response = response.ToResponse(true, string.Empty, (IList<MachineLsResponse>)list);
      return this;
    }
  }
}
