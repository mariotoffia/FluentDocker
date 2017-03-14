namespace Ductus.FluentDocker.Common
{
  public sealed class Option<T> where T : class
  {
    public Option(T value)
    {
      Value = value;
      HasValue = null != value;
    }

    public T Value { get; }
    public bool HasValue { get; }

    public static implicit operator T(Option<T> option)
    {
      return option.Value;
    }

    public static explicit operator Option<T>(T value)
    {
      return new Option<T>(value);
    }
  }
}