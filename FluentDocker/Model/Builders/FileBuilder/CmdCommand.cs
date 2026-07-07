#nullable enable
namespace FluentDocker.Model.Builders.FileBuilder
{
  public sealed class CmdCommand(string cmd, params string[] args) : ICommand
  {
    public string Cmd { get; } = cmd;
    public string[] Arguments { get; } = args ?? [];

    public override string ToString()
    {
      return $"CMD {DockerfileJson.Array([Cmd, .. Arguments])}";
    }
  }
}
