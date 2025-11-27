using Ductus.FluentDocker.Model.Common;

namespace Ductus.FluentDocker.Model.Builders.FileBuilder
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
