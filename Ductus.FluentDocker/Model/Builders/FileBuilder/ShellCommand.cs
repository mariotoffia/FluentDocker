using Ductus.FluentDocker.Model.Common;

namespace Ductus.FluentDocker.Model.Builders.FileBuilder
{
  public sealed class ShellCommand : ICommand
  {
    public ShellCommand(string shell, params string[] args)
    {
      Shell = shell;
      Arguments = null == args ? new string[0] : args;
    }

    public string Shell { get; }
    public string[] Arguments { get; }

    public override string ToString()
    {
      return $"SHELL [\"{Shell}{string.Join("\",\"", Arguments)}\"]";
    }
  }
}
