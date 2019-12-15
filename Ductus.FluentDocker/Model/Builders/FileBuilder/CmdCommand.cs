namespace Ductus.FluentDocker.Model.Builders.FileBuilder
{
  public sealed class CmdCommand : ICommand
  {
    public CmdCommand(string cmd, params string[] args)
    {
      Cmd = cmd;
      Arguments = null == args ? new string[0] : args;
    }

    public string Cmd { get; }
    public string[] Arguments { get; }

    public override string ToString()
    {
      return $"CMD [\"{Cmd}{string.Join("\",\"", Arguments)}\"]";
    }
  }
}
