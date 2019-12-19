using System.Collections.Generic;
using System.Linq;
using Ductus.FluentDocker.Model.Containers;
using Newtonsoft.Json;

namespace Ductus.FluentDocker.Executors.Parsers
{
  public sealed class ClientInspectContainersResponseParser : IProcessResponseParser<IList<Container>>
  {
    public CommandResponse<IList<Container>> Response { get; private set; }

    public IProcessResponse<IList<Container>> Process(ProcessExecutionResult response)
    {
      if (response.ExitCode != 0)
      {
        Response = response.ToErrorResponse((IList<Container>)new List<Container>());
        return this;
      }

      if (string.IsNullOrEmpty(response.StdOut))
      {
        Response = response.ToResponse(false, "Empty response", (IList<Container>)new List<Container>());
        return this;
      }

      var containers = JsonConvert.DeserializeObject<Container[]>(response.StdOut);
      foreach (var c in containers)
      {
        c.Name = TrimIfBeginsWithSlash(c.Name);
      }

      Response = response.ToResponse(true, string.Empty, (IList<Container>)containers.ToList());
      return this;
    }

    private static string TrimIfBeginsWithSlash(string name)
    {
      if (!string.IsNullOrEmpty(name) && name.StartsWith("/"))
        return name.Substring(1);
      return name;
    }
  }
}
