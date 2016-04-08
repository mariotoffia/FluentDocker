using Ductus.FluentDocker.Model;
using Newtonsoft.Json;

namespace Ductus.FluentDocker.Executors.Parsers
{
  public sealed class ClientContainerInspectCommandResponder : IProcessResponseParser<Container>
  {
    public CommandResponse<Container> Response { get; private set; }

    public IProcessResponse<Container> Process(ProcessExecutionResult response)
    {
      if (string.IsNullOrEmpty(response.StdOut))
      {
        Response = response.ToResponse(false, "Empty response", new Container());
        return this;
      }

      Response = response.ToResponse(true, string.Empty, JsonConvert.DeserializeObject<Container>(response.StdOut));
      return this;
    }
  }
}