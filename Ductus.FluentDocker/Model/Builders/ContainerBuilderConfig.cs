using System;
using System.Collections.Generic;
using Ductus.FluentDocker.Model.Containers;

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
    public Tuple<string/*port/proto*/,int/*time*/> WaitForPort { get; set; }
    public List<Tuple<string/*host*/,string/*container*/>> CopyToContainerStart { get; set; }
    public List<Tuple<string/*host*/, string/*container*/>> CopyFromContainerStart { get; set; }
    public List<Tuple<string/*host*/, string/*container*/>> CopyToContainerStop { get; set; }
    public List<Tuple<string/*host*/, string/*container*/>> CopyFromContainerStop { get; set; }
  }
}