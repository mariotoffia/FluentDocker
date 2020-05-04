using System;

namespace Ductus.FluentDocker.Model.Builders.FileBuilder
{
  public sealed class CmdCommand : ICommand
  {
    public CmdCommand(string cmd, params string[] args)
    {
      Cmd = cmd;
      Arguments = args ?? Array.Empty<string>();
    }

    public string Cmd { get; }
    public string[] Arguments { get; }

    public override string ToString()
    {
      return $"CMD [\"{Cmd}{string.Join("\",\"", Arguments)}\"]";
    }
  }
}
