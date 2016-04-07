using System.Net;
using Ductus.FluentDocker.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Ductus.FluentDocker.Executors.Parsers
{
  public sealed class MachineInspectResponseParser : IProcessResponseParser<MachineConfiguration>
  {
    public MachineConfiguration Response { get; private set; }

    public IProcessResponse<MachineConfiguration> Process(string response)
    {
      if (string.IsNullOrEmpty(response))
      {
        Response = new MachineConfiguration {AuthConfig = new MachineAuthConfig()};
        return this;
      }

      var j = JObject.Parse(response);

      var str = j["HostOptions"]["AuthOptions"].ToString();
      var ip = j["Driver"]["IPAddress"].Value<string>();
      var authConfig = JsonConvert.DeserializeObject<MachineAuthConfig>(str);

      Response = new MachineConfiguration
      {
        AuthConfig = authConfig,
        IpAddress = string.IsNullOrEmpty(ip) ? IPAddress.None : IPAddress.Parse(ip),
        DriverName = j["DriverName"].Value<string>(),
        MemorySizeMb = j["Driver"]["Memory"].Value<int>(),
        Name = j["Name"].Value<string>(),
        RequireTls = j["HostOptions"]["EngineOptions"]["TlsVerify"].Value<bool>(),
        StorageSizeMb = j["Driver"]["DiskSize"].Value<int>(),
        CpuCount = j["Driver"]["CPU"].Value<int>(),
        StorePath = j["Driver"]["StorePath"].Value<string>()
      };
      return this;
    }
  }
}