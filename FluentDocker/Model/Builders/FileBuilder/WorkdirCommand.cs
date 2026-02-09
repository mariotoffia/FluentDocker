using FluentDocker.Model.Common;

namespace FluentDocker.Model.Builders.FileBuilder
{
  public sealed class WorkdirCommand : ICommand
  {
    public WorkdirCommand(string workdir) => Workdir = workdir;

    public string Workdir { get; }

    public override string ToString()
    {
      return $"WORKDIR {Workdir}";
    }
  }
}
