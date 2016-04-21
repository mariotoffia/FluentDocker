using System;
using System.Collections.Generic;
using Ductus.FluentDocker.Model.Common;
using Ductus.FluentDocker.Model.Containers;
using Ductus.FluentDocker.Services;

namespace Ductus.FluentDocker.Model.Builders
{
  public sealed class ContainerBuilderConfig
  {
    public ContainerBuilderConfig()
    {
      CreateParams = new ContainerCreateParams();
    }

    public ContainerCreateParams CreateParams { get; }
    public string Image { get; set; }
    public bool StopOnDispose { get; set; } = true;
    public bool DeleteOnDispose { get; set; } = true;
    public string Command { get; set; }
    public string[] Arguments { get; set; }

    public List<Tuple<TemplateString /*host*/, TemplateString /*container*/>> CopyFromContainerBeforeDispose { get; set;
    }

    public Tuple<TemplateString /*host*/, bool /*explode*/, Func<IContainerService, bool> /*condition*/>
      ExportContainerOnDispose { get; set; }
  }
}