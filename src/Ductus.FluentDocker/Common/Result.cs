namespace Ductus.FluentDocker.Common
{
  public sealed class Result<T>
  {
    internal Result(bool success, T value, string log, string error)
    {
      Value = value;
      IsSuccess = success;
      IsFailure = !success;
      Log = log;
      Error = error;
    }

    public bool IsSuccess { get; }
    public bool IsFailure { get; }
    public T Value { get; }
    public string Log { get; }
    public string Error { get; }
  }
}
