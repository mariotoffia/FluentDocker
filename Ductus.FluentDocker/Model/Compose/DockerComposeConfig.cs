using System;
using System.Collections.Generic;
using Ductus.FluentDocker.Model.Images;
using Ductus.FluentDocker.Model.Common;

namespace Ductus.FluentDocker.Model.Compose
{
  public class DockerComposeConfig
  {
    /// <summary>
    ///   Fully qualified path to the docker-compose file.
    /// </summary>
    public IList<string> ComposeFilePath { get; set; } = new List<string>();

    public ComposeVersion ComposeVersion { get; set; }
    public bool AlwaysPull { get; set; }
    public bool ForceRecreate { get; set; }
    public bool NoRecreate { get; set; }
    public bool NoBuild { get; set; }
    public bool ForceBuild { get; set; }
    public TimeSpan TimeoutSeconds { get; set; }
    public bool RemoveOrphans { get; set; }
    public string AlternativeServiceName { get; set; }
    public bool UseColor { get; set; }
    public bool KeepVolumes { get; set; }
    public ImageRemovalOption ImageRemoval { get; set; }
    public string[] Services { get; set; }
    public bool StopOnDispose { get; set; } = true;
    public bool KeepContainers { get; set; }
    public IDictionary<string, string> EnvironmentNameValue { get; set; } = new Dictionary<string, string>();
    public TemplateString ProjectDirectory {get;set;}

    public IDictionary<string, ContainerSpecificConfig> ContainerConfiguration { get; } =
      new Dictionary<string, ContainerSpecificConfig>();
  }
}
