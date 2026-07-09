#nullable enable
using FluentDocker.Model.Common;

namespace FluentDocker.Model.Builders.FileBuilder
{
  public sealed class WorkdirCommand(string workdir) : ICommand
  {
    public string Workdir { get; } = DockerfileInstructionGuard.Require(
        workdir, "WORKDIR", "path", "WORKDIR requires a path.");

    public override string ToString()
    {
      return $"WORKDIR {Workdir}";
    }
  }
}
