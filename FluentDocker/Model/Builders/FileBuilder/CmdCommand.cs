using System;
using System.Linq;

namespace FluentDocker.Model.Builders.FileBuilder
{
  public sealed class CmdCommand(string cmd, params string[] args) : ICommand
  {
    public string Cmd { get; } = cmd;
    public string[] Arguments { get; } = [.. (args ?? []).Select(arg => $"\"{arg}\"")];

    public override string ToString()
    {
      var args = string.Join(", ", Arguments);

      args = string.IsNullOrEmpty(args) ? "" : $", {args}";

      return $"CMD [\"{Cmd}\"{args}]";
    }
  }
}
