using System.Collections.Generic;
using System.Linq;
using Ductus.FluentDocker.Model.Containers;
using Ductus.FluentDocker.Model.Stacks;

namespace Ductus.FluentDocker.Executors.Parsers
{
  public class StackPsResponseParser : IProcessResponseParser<IList<StackPsResponse>>
  {
    public CommandResponse<IList<StackPsResponse>> Response { get; private set; }

    public IProcessResponse<IList<StackPsResponse>> Process(ProcessExecutionResult response)
    {
      var list = new List<StackPsResponse>();
      var rows = response.StdOutAsArry;
      if (rows.Length > 0)
        list.AddRange(from row in rows
                      select row.Split(';')
          into s
                      where s.Length == 3
                      select new StackPsResponse
                      {
                        Id = s[0],
                        Stack = s[1].Split('_')[0],
                        Name = s[1].Split('_')[1],
                        Image = s[2].Split(':')[0],
                        ImageVersion = s[2].Split(':')[1],
                        Node = s[3],
                        DesiredState = s[4],
                        CurrentState = s[5],
                        Error = s[6] ?? string.Empty,
                        Ports = s[7] ?? string.Empty
                      });

      Response = response.ToResponse(true, string.Empty, (IList<StackPsResponse>)list);
      return this;
    }
  }
}
