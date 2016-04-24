using System.Collections.Generic;
using Ductus.FluentDocker.Model.Containers;

namespace Ductus.FluentDocker.Model.Builders
{
  public sealed class ImageBuilderConfig
  {
    public bool VerifyExistence { get; set; }
    public bool ForeRebuild { get; set; }
    public string ImageName { get; set; }
    public ContainerBuildParams Params { get; } = new ContainerBuildParams();

  }
}
