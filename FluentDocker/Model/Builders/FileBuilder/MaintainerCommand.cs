namespace FluentDocker.Model.Builders.FileBuilder
{
  public sealed class MaintainerCommand(string maintainer) : ICommand
  {
    public string Maintainer { get; } = maintainer;

    public override string ToString()
    {
      return $"MAINTAINER {Maintainer}";
    }
  }
}
