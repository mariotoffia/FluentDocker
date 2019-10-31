using Ductus.FluentDocker.Model.Common;

namespace Ductus.FluentDocker.Model.Builders.FileBuilder
{
  public sealed class ExposeCommand : ICommand
  {
    public ExposeCommand(params int []ports)
    {
      Ports = null == ports ? new int[0] : ports;
    }

    public int []Ports { get; }

    public override string ToString()
    {
      return $"EXPOSE {string.Join(",", Ports)}";
    }
  }
}
