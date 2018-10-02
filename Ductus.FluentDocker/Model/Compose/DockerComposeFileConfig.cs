using System.Collections.Generic;
using Ductus.FluentDocker.Model.Containers;

namespace Ductus.FluentDocker.Model.Compose
{
  public sealed class DockerComposeFileConfig : DockerComposeConfig
  {
    public string Version { get; set; } = "3.3";
    public IList<ComposeServiceDefinition> ServiceDefinitions { get; } = new List<ComposeServiceDefinition>();
    public IList<ComposeVolumeDefinition> VolumeDefinitions { get; } = new List<ComposeVolumeDefinition>();
  }
}