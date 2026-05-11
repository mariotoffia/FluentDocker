namespace FluentDocker.Model.Containers
{
  /// <summary>
  /// A item with a <see cref="Ulimit"/> and at least a soft limit but may include a hard as well.
  /// </summary>
  public class ULimitItem(Ulimit ulimit, string soft, string hard = null)
  {
    public Ulimit Ulimit { get; } = ulimit;
    public string Soft { get; } = soft;
    public string Hard { get; } = hard;

    public override string ToString()
    {
      return !string.IsNullOrEmpty(Hard) ? $"{Ulimit.ToString().ToLower()}={Soft}:{Hard}" : $"{Ulimit}={Soft}";
    }
  }
}
