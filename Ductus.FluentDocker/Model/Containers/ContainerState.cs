using System;

// ReSharper disable InconsistentNaming

namespace Ductus.FluentDocker.Model.Containers
{
  public sealed class ContainerState
  {
    public string Status { get; set; }
    public bool Running { get; set; }
    public bool Paused { get; set; }
    public bool Restarting { get; set; }
    public bool OOMKilled { get; set; }
    public bool Dead { get; set; }
    public int Pid { get; set; }
    public long ExitCode { get; set; }
    public string Error { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime FinishedAt { get; set; }
    public Health Health { get; set; }
  }
}
