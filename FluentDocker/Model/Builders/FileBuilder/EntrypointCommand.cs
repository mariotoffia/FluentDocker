using System;

namespace FluentDocker.Model.Builders.FileBuilder
{
  public sealed class EntrypointCommand(string executable, params string[] args) : ICommand
  {
    public string Executable { get; } = executable;
    public string[] Arguments { get; } = args ?? [];

    public override string ToString()
    {
      if (Arguments.Length == 0)
      {
        return $"ENTRYPOINT [\"{Executable}\"]";
      }

      return $"ENTRYPOINT [\"{Executable}\",\"{string.Join("\",\"", Arguments)}\"]";
    }
  }
}
