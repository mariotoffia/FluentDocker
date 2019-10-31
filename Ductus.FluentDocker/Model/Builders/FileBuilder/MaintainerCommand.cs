namespace Ductus.FluentDocker.Model.Builders.FileBuilder
{
  public sealed class MaintainerCommand : ICommand
  {
    public MaintainerCommand(string maintainer)
    {
      Maintainer = maintainer;
    }

    public string Maintainer { get; }

    public override string ToString()
    {
      return $"MAINTAINER {Maintainer}";
    }
  }
}
