using FluentDocker.Model.Common;

namespace FluentDocker.Model.Builders.FileBuilder
{
  public sealed class RunCommand : ICommand
  {
    public RunCommand(TemplateString run)
    {
      Run = run;
    }

    public TemplateString Run { get; }

    public override string ToString()
    {
      return $"RUN {Run}";
    }
  }
}
