using Ductus.FluentDocker.Model.Common;

namespace Ductus.FluentDocker.Model.Builders
{
  public sealed class AddCommand
  {
    public TemplateString Source { get; set; }
    public TemplateString Destination { get; set; }

    public override string ToString()
    {
      return $"ADD {Source} {Destination}";
    }
  }
}
