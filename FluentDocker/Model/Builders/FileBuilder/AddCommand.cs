using FluentDocker.Model.Common;

namespace FluentDocker.Model.Builders.FileBuilder
{
  public sealed class AddCommand(TemplateString source, TemplateString destination) : ICommand
  {
    public TemplateString Source { get; internal set; } = source;
    public TemplateString Destination { get; } = destination;

    public override string ToString()
    {
      return $"ADD {Source} {Destination}";
    }
  }
}
