using System;

namespace Ductus.FluentDocker.Model.Networks
{
  public sealed class NetworkRow
  {
    public string Id { get; set; }
    public string Name { get; set; }
    public string Driver { get; set; }
    public string Scope { get; set; }

    // ReSharper disable once InconsistentNaming
    public bool IPv6 { get; set; }

    public bool Internal { get; set; }
    public DateTime Created { get; set; }
  }
}