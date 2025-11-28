using FluentDocker.Model.Common;

namespace FluentDocker.Model.Builders.FileBuilder
{
  public sealed class AddCommand : ICommand
  {
    public AddCommand(TemplateString source, TemplateString destination)
    {
      Source = source;
      Destination = destination;
    }

    public TemplateString Source { get; internal set; }
    public TemplateString Destination { get; }

    public override string ToString()
    {
      return $"ADD {Source} {Destination}";
    }
  }
}
