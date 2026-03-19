namespace FluentDocker.Common
{
  /// <summary>
  /// Represents the result of a kernel or builder operation that can succeed or fail,
  /// carrying a value, log output, and error message.
  /// For driver-level command results, use <see cref="FluentDocker.Model.Drivers.CommandResponse{T}"/> instead.
  /// </summary>
  /// <typeparam name="T">The result value type.</typeparam>
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

    /// <summary>True if the operation succeeded.</summary>
    public bool IsSuccess { get; }

    /// <summary>True if the operation failed.</summary>
    public bool IsFailure { get; }

    /// <summary>The result value. May be default if the operation failed.</summary>
    public T Value { get; }

    /// <summary>Combined stdout/stderr log output from the operation.</summary>
    public string Log { get; }

    /// <summary>Error message if the operation failed; empty on success.</summary>
    public string Error { get; }
  }
}
