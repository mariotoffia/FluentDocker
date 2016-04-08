using System.Collections.Generic;
using System.Linq;
using Ductus.FluentDocker.Model;

namespace Ductus.FluentDocker.Executors.Parsers
{
  public sealed class MachineCreateResponseParser : IProcessResponseParser<string>
  {
    public CommandResponse<string> Response { get; private set; }

    public IProcessResponse<string> Process(ProcessExecutionResult response)
    {
      bool success =
        response.StdOutAsArry.All(
          line => !line.StartsWith("Error") && !line.StartsWith("Can't remove") && !line.StartsWith("Incorrect Usage."));

      Response = response.ToResponse(success, "Error Creating Machine", string.Empty);
      return this;
    }
  }
}
