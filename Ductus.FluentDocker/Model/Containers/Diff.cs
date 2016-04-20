namespace Ductus.FluentDocker.Model.Containers
{
  public sealed class Diff
  {
    public DiffType Type { get; set; }
    public string Item { get; set; }
  }
}
