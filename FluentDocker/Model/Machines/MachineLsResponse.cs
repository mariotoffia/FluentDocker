using System;
using FluentDocker.Services;

namespace FluentDocker.Model.Machines
{
  public sealed class MachineLsResponse
  {
    public string Name { get; set; }
    public ServiceRunningState State { get; set; }
    public Uri Docker { get; set; }
  }
}
