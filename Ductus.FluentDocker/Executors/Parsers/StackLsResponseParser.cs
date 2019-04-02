using System.Collections.Generic;
using System.Linq;
using Ductus.FluentDocker.Model.Containers;
using Ductus.FluentDocker.Model.Stacks;

namespace Ductus.FluentDocker.Executors.Parsers
{
  public class StackLsResponseParser : IProcessResponseParser<IList<StackLsResponse>>
  {
    public CommandResponse<IList<StackLsResponse>> Response { get; private set; }

    public IProcessResponse<IList<StackLsResponse>> Process(ProcessExecutionResult response)
    {
      var list = new List<StackLsResponse>();
      var rows = response.StdOutAsArry;
      
      if (rows.Length > 0)
      {
        list.AddRange(from row in rows
          select row.Split(';')
          into s
          where s.Length == 3
          select new StackLsResponse
          {
            Name = s[0],
            Services = null == s[1] ? -1 : int.Parse(s[1]),
            Orchestrator = StackLsResponse.ToOrchestrator(s[2]),
            Namespace = s[3]
          });
      }

      Response = response.ToResponse(true, string.Empty, (IList<StackLsResponse>) list);
      return this;
    }
  }  
}