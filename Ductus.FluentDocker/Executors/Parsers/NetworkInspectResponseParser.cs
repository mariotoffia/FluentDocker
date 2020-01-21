using Ductus.FluentDocker.Model.Containers;
using Ductus.FluentDocker.Model.Networks;
using Newtonsoft.Json;

namespace Ductus.FluentDocker.Executors.Parsers
{
  public sealed class NetworkInspectResponseParser : IProcessResponseParser<NetworkConfiguration>
  {
    public CommandResponse<NetworkConfiguration> Response { get; private set; }

    public IProcessResponse<NetworkConfiguration> Process(ProcessExecutionResult response)
    {
      if (string.IsNullOrEmpty(response.StdOut))
      {
        Response = response.ToResponse(false, "No response", new NetworkConfiguration());
        return this;
      }

      var resp = JsonConvert.DeserializeObject<NetworkConfiguration[]>(response.StdOut);
      if (null == resp || 0 == resp.Length)
      {
        Response = response.ToResponse(false, "No response", new NetworkConfiguration());
        return this;
      }

      Response = response.ToResponse(true, string.Empty, resp[0]);
      return this;
    }
  }
}
