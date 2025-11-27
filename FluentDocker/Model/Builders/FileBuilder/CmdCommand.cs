using System;
using System.Linq;

namespace Ductus.FluentDocker.Model.Builders.FileBuilder
{
  public sealed class CmdCommand : ICommand
  {
    public CmdCommand(string cmd, params string[] args)
    {
      Cmd = cmd;
      Arguments = (args ?? Array.Empty<string>())
        .Select(arg => $"\"{arg}\"")
        .ToArray();
    }

    public string Cmd { get; }
    public string[] Arguments { get; }

    public override string ToString()
    {
      var args = string.Join(", ", Arguments);

      args = string.IsNullOrEmpty(args) ? "" : $", {args}";

      return $"CMD [\"{Cmd}\"{args}]";
    }
  }
}
