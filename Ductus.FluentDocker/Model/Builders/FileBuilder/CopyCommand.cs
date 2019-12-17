namespace Ductus.FluentDocker.Model.Builders.FileBuilder
{
  public sealed class CopyCommand : ICommand
  {
    public CopyCommand(string from, string to)
    {
      From = from;
      To = to;
    }

    public string From { get; }
    public string To { get; }

    public override string ToString()
    {
      return $"COPY {From} {To}";
    }
  }
}
