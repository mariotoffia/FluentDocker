using FluentDocker.Model.Common;

namespace FluentDocker.Model.Builders.FileBuilder
{
  public sealed class RunCommand(TemplateString run) : ICommand
  {
    public TemplateString Run { get; } = run;

    public override string ToString()
    {
      return $"RUN {Run}";
    }
  }
}
