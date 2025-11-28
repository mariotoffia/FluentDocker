using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.Collections.Generic;

namespace FluentDocker.Model.Containers
{
  public class Health
  {
    [JsonConverter(typeof(StringEnumConverter))]
    public HealthState Status { get; set; }
    
    public int FailingStreak { get; set; }
    
    public List<HealthLog> Log { get; set; }
  }

  public class HealthLog
  {
    public string Start { get; set; }
    public string End { get; set; }
    public int ExitCode { get; set; }
    public string Output { get; set; }
  }
}
