namespace Ductus.FluentDocker.Model.Containers
{
  /// <summary>
  /// A item with a <see cref="Ulimit"/> and atleast a soft limit but may include a hard as well.
  /// </summary>
  public class ULimitItem
  {
    public ULimitItem(Ulimit ulimit, string soft, string hard = null)
    {
      Ulimit = ulimit;
      Soft = soft;
      Hard = hard;
    }
    
    public Ulimit Ulimit { get; }
    public string Soft { get; }
    public string Hard { get; }

    public override string ToString()
    {
      return !string.IsNullOrEmpty(Hard) ? $"{Ulimit}={Soft}:{Hard}" : $"{Ulimit}={Soft}";
    }
  }
}