using System.Collections.Generic;
using Ductus.FluentDocker.Model.Containers;

namespace Ductus.FluentDocker.Model.Compose
{
  // https://docs.docker.com/compose/compose-file/#service-configuration-reference
  public sealed class ComposeServiceDefinition
  {
    public string Name { get; set; }
    public string Image { get; set; }
    public IList<string> Volumes { get; } = new List<string>();
    public RestartPolicy RestartPolicy { get; set; }
    public IDictionary<string, string> Environment { get; set; }
    public IList<string> Dependencies { get; set; } = new List<string>();
    public IList<string> Ports { get; set; } = new List<string>();
  }
}