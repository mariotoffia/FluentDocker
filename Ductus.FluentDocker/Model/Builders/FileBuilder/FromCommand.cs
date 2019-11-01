namespace Ductus.FluentDocker.Model.Builders.FileBuilder
{
  public sealed class FromCommand : ICommand
  {
    public FromCommand(string @from)
    {
      From = @from;
    }

    public string From { get; }

    public override string ToString()
    {
      return $"FROM {From}";
    }
  }
}
