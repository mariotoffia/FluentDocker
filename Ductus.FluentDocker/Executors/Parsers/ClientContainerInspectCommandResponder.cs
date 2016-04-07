using Ductus.FluentDocker.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Ductus.FluentDocker.Executors.Parsers
{
  public sealed class ClientContainerInspectCommandResponder : IProcessResponseParser<Container>
  {
    public Container Response { get; private set; }
    public IProcessResponse<Container> Process(string response)
    {
      if (string.IsNullOrEmpty(response))
      {
        Response = new Container();
        return this;
      }

      Response = JsonConvert.DeserializeObject<Container>(response);
      return this;
    }
  }
}
