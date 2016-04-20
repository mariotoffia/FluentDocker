// ReSharper disable InconsistentNaming
namespace Ductus.FluentDocker.Model.Containers
{
  public sealed class ContainerMount
  {
    public string Name { get; set; }
    public string Source { get; set; }
    public string Destination { get; set; }
    public string Driver { get; set; }
    public string Mode { get; set; }
    public bool RW { get; set; }
    public string Propagation { get; set; }
  }
}
