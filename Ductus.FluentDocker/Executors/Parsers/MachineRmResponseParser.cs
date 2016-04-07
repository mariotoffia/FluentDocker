using System;
using System.Collections.Generic;
using System.Linq;
using Ductus.FluentDocker.Model;

namespace Ductus.FluentDocker.Executors.Parsers
{
  public sealed class MachineRmResponseParser : IProcessResponseParser<CommandResponse>
  {
    public CommandResponse Response { get; private set; }
    public IProcessResponse<CommandResponse> Process(string response)
    {
      var log = string.IsNullOrEmpty(response) ? new List<string>() : new List<string>(response.Split('\n'));
      bool success = log.All(line => !line.StartsWith("Error") && !line.StartsWith("Can't remove") && !line.StartsWith("Incorrect Usage."));

      Response = new CommandResponse(success,log);
      return this;
    }
  }
}
