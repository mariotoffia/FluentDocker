using FluentDocker.Model.Common;

namespace FluentDocker.Model.Builders.FileBuilder
{
  public sealed class WorkdirCommand(string workdir) : ICommand
  {
    public string Workdir { get; } = workdir;

    public override string ToString()
    {
      return $"WORKDIR {Workdir}";
    }
  }
}
