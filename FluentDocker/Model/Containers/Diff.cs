namespace FluentDocker.Model.Containers
{
  [System.Obsolete("Unused by FluentDocker and scheduled for removal in v4. Use IContainerDriver DiffAsync filesystem changes instead.")]
  public sealed class Diff
  {
    public DiffType Type { get; set; }
    public string Item { get; set; }
  }
}
