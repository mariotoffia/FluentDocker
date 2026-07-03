namespace FluentDocker.Model.Builders.FileBuilder
{
  public sealed class EntrypointCommand(string executable, params string[] args) : ICommand
  {
    public string Executable { get; } = executable;
    public string[] Arguments { get; } = args ?? [];

    public override string ToString()
    {
      return $"ENTRYPOINT {DockerfileJson.Array([Executable, .. Arguments])}";
    }
  }
}
