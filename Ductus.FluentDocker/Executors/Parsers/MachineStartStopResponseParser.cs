using System.Linq;
using Ductus.FluentDocker.Model.Containers;

namespace Ductus.FluentDocker.Executors.Parsers
{
  public sealed class MachineStartStopResponseParser : IProcessResponseParser<string>
  {
    public CommandResponse<string> Response { get; private set; }

    public IProcessResponse<string> Process(ProcessExecutionResult response)
    {
      var success =
        response.StdOutAsArry.All(
          line => !line.StartsWith("Host does not exist") && !line.StartsWith("Incorrect Usage."));

      Response = response.ToResponse(success, string.Empty, string.Empty);
      return this;
    }
  }
}
