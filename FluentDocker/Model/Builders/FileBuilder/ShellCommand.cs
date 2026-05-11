using System;

namespace FluentDocker.Model.Builders.FileBuilder
{
  public sealed class ShellCommand(string shell, params string[] args) : ICommand
  {
    public string Shell { get; } = shell;
    public string[] Arguments { get; } = args ?? [];

    public override string ToString()
    {
      if (Arguments.Length == 0)
      {
        return $"SHELL [\"{Shell}\"]";
      }

      return $"SHELL [\"{Shell}\",\"{string.Join("\",\"", Arguments)}\"]";
    }
  }
}
