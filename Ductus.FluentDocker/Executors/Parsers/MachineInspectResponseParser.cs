using System.Linq;
using System.Net;
using Ductus.FluentDocker.Model.Containers;
using Ductus.FluentDocker.Model.Machines;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Ductus.FluentDocker.Executors.Parsers
{
  public sealed class MachineInspectResponseParser : IProcessResponseParser<MachineConfiguration>
  {
    public CommandResponse<MachineConfiguration> Response { get; private set; }

    public IProcessResponse<MachineConfiguration> Process(ProcessExecutionResult response)
    {
      if (string.IsNullOrEmpty(response.StdOut))
      {
        Response = response.ToResponse(false, "No response",
          new MachineConfiguration {AuthConfig = new MachineAuthConfig()});
        return this;
      }

      var j = JObject.Parse(response.StdOut);

      var str = j["HostOptions"]["AuthOptions"].ToString();
      var ip = j["Driver"]["IPAddress"].Value<string>();
      var authConfig = JsonConvert.DeserializeObject<MachineAuthConfig>(str);

      int memsize = 0;
      if (null != j["Driver"]["Memory"])
      {
        memsize = j["Driver"]["Memory"].Value<int>();
      }

      if (null != j["Driver"]["MemSize"])
      {
        memsize = j["Driver"]["MemSize"].Value<int>();
      }
      
      
      var config = new MachineConfiguration
      {
        AuthConfig = authConfig,
        IpAddress = string.IsNullOrEmpty(ip) ? IPAddress.None : IPAddress.Parse(ip),
        DriverName = j["DriverName"].Value<string>(),
        MemorySizeMb = memsize,
        Name = j["Name"].Value<string>(),
        RequireTls = j["HostOptions"]["EngineOptions"]["TlsVerify"].Value<bool>(),
        StorageSizeMb = j["Driver"]["DiskSize"].Value<int>(),
        CpuCount = j["Driver"]["CPU"].Value<int>(),
        StorePath = j["Driver"]["StorePath"].Value<string>()
      };

      Response = response.ToResponse(true, string.Empty, config);
      return this;
    }
  }
}