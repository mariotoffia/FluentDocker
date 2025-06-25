using System.Collections.Generic;
using System.Linq;

namespace Ductus.FluentDocker.Model.Builders.FileBuilder
{
  public sealed class ExposeCommand : ICommand
  {
    public ExposeCommand(params int[] ports)
      => Ports = ports.Select(p => p.ToString()) ?? Enumerable.Empty<string>();

    public ExposeCommand(params string[] ports)
      => Ports = ports ?? Enumerable.Empty<string>();

    public IEnumerable<string> Ports { get; }

    public override string ToString()
    {
      return $"EXPOSE {string.Join(" ", Ports)}";
    }
  }
}
