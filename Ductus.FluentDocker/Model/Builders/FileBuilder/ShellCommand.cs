using System;

namespace Ductus.FluentDocker.Model.Builders.FileBuilder
{
  public sealed class ShellCommand : ICommand
  {
    public ShellCommand(string shell, params string[] args)
    {
      Shell = shell;
      Arguments = args ?? Array.Empty<string>();
    }

    public string Shell { get; }
    public string[] Arguments { get; }

    public override string ToString()
    {
      if (Arguments.Length == 0) {
        return $"SHELL [\"{Shell}\"]"; 
      }

      return $"SHELL [\"{Shell}\",\"{string.Join("\",\"", Arguments)}\"]";
    }
  }
}
