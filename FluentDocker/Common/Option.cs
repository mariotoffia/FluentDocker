namespace FluentDocker.Common
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

    public static implicit operator T(Option<T> option) => option.Value;

    public static explicit operator Option<T>(T value) => new Option<T>(value);
  }
}
