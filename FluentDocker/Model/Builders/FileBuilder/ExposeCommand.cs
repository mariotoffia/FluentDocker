#nullable enable
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FluentDocker.Model.Builders.FileBuilder
{
  public sealed class ExposeCommand : ICommand
  {
    public ExposeCommand(params int[] ports)
    => Ports = (ports ?? []).Select(p => p.ToString(CultureInfo.InvariantCulture));

    public ExposeCommand(params string[] ports)
      => Ports = ports ?? Enumerable.Empty<string>();

    public IEnumerable<string> Ports { get; }

    public override string ToString()
    {
      return $"EXPOSE {string.Join(" ", Ports)}";
    }
  }
}
