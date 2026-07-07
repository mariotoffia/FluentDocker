#nullable enable
namespace FluentDocker.Model.Builders.FileBuilder
{
  public sealed class ShellCommand(string shell, params string[] args) : ICommand
  {
    public string Shell { get; } = shell;
    public string[] Arguments { get; } = args ?? [];

    public override string ToString()
    {
      return $"SHELL {DockerfileJson.Array([Shell, .. Arguments])}";
    }
  }
}
