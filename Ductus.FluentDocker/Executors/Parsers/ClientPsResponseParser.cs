using System.Collections.Generic;
using Ductus.FluentDocker.Model;

namespace Ductus.FluentDocker.Executors.Parsers
{
  public sealed class ClientPsResponseParser : IProcessResponseParser<CommandResponse>
  {
    public CommandResponse Response { get; private set; }
    public IProcessResponse<CommandResponse> Process(string response)
    {
      if (string.IsNullOrEmpty(response))
      {
        Response = new CommandResponse(false, new List<string>());
        return this;
      }

      Response = new CommandResponse(true, new List<string>(response.Split('\n')));
      return this;
    }
  }
}
