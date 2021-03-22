using System;

namespace Ductus.FluentDocker.Model.Builders.FileBuilder
{
  public sealed class EntrypointCommand : ICommand
  {
    public EntrypointCommand(string executable, params string[] args)
    {
      Executable = executable;
      Arguments = args ?? Array.Empty<string>();
    }

    public string Executable { get; }
    public string[] Arguments { get; }

    public override string ToString()
    {
      if (Arguments.Length == 0) {
        return $"ENTRYPOINT [\"{Executable}\"]"; 
      }

      return $"ENTRYPOINT [\"{Executable}\",\"{string.Join("\",\"", Arguments)}\"]";
    }
  }
}
