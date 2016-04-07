using System.Collections.Generic;
using System.Linq;
using Ductus.FluentDocker.Model;

namespace Ductus.FluentDocker.Executors.Parsers
{
  public sealed class MachineStartStopResponseParser : IProcessResponseParser<CommandResponse>
  {
    public CommandResponse Response { get; private set; }

    public IProcessResponse<CommandResponse> Process(string response)
    {
      var log = string.IsNullOrEmpty(response) ? new List<string>() : new List<string>(response.Split('\n'));
      bool success = log.All(line => !line.StartsWith("Host does not exist") && !line.StartsWith("Incorrect Usage."));

      Response = new CommandResponse(success, log);
      return this;
    }
  }
}
