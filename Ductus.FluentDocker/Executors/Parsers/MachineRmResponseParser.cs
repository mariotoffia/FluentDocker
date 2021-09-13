using System.Linq;
using Ductus.FluentDocker.Model.Containers;

namespace Ductus.FluentDocker.Executors.Parsers
{
  public sealed class MachineRmResponseParser : IProcessResponseParser<string>
  {
    public CommandResponse<string> Response { get; private set; }

    public IProcessResponse<string> Process(ProcessExecutionResult response)
    {
      var success =
        response.StdOutAsArray.All(
          line => !line.StartsWith("Error") && !line.StartsWith("Can't remove") && !line.StartsWith("Incorrect Usage."));

      Response = response.ToResponse(success, string.Empty, string.Empty);
      return this;
    }
  }
}
