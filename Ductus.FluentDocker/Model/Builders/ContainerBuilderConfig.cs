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

    public bool VerifyExistence { get; set; }
    public ContainerCreateParams CreateParams { get; }
    public string Image { get; set; }
    public bool IsWindowsImage { get; set; }
    public bool StopOnDispose { get; set; } = true;
    public bool DeleteOnDispose { get; set; } = true;
    public bool DeleteVolumeOnDispose { get; set; } = false;
    public bool DeleteNamedVolumeOnDispose { get; set; } = false;
    public string Command { get; set; }
    public string[] Arguments { get; set; }
    public Tuple<string /*portAndProto*/, long /*waitTimeout*/> WaitForPort { get; set; }
    public Tuple<string /*process*/, long /*waitTimeout*/> WaitForProcess { get; set; }
    public List<Tuple<TemplateString /*host*/, TemplateString /*container*/>> CpToOnStart { get; set; }
    public List<Tuple<TemplateString /*host*/, TemplateString /*container*/>> CpFromOnDispose { get; set; }

    public Tuple<TemplateString /*host*/, bool /*explode*/,
      Func<IContainerService, bool> /*condition*/> ExportOnDispose { get; set; }
    public List<INetworkService> Networks { get; set; }
    public List<string> NetworkNames { get; set; }
    public List<string> ExecuteOnRunningArguments { get; set; }
    public List<string> ExecuteOnDisposingArguments { get; set; }
  }
}