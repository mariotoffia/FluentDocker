#nullable enable
using FluentDocker.Model.Common;

namespace FluentDocker.Model.Builders.FileBuilder
{
  public sealed class RunCommand(TemplateString run) : ICommand
  {
    public string Run { get; } = DockerfileInstructionGuard.Require(
        run, "RUN", "command", "RUN requires a command.");

    public override string ToString()
    {
      return $"RUN {Run}";
    }
  }
}
