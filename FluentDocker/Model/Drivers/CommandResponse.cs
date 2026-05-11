namespace FluentDocker.Model.Drivers
{
  /// <summary>
  /// Represents the result of a driver command execution.
  /// All driver interfaces return <see cref="CommandResponse{T}"/> from their operations.
  /// For kernel/builder build results, use <see cref="FluentDocker.Common.Result{T}"/> instead.
  /// Properties are init-only; use the <see cref="Ok(T)"/> and <see cref="Fail(string, string, int)"/> factory methods.
  /// </summary>
  /// <typeparam name="T">The type of data returned by the command</typeparam>
#pragma warning disable CA1000 // Static members on generic type — factory pattern is intentional API design
  public class CommandResponse<T>
  {
    /// <summary>
    /// Indicates whether the command executed successfully.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// The data returned by the command (if successful).
    /// </summary>
    public T Data { get; init; }

    /// <summary>
    /// Error message (if not successful).
    /// </summary>
    public string Error { get; init; }

    /// <summary>
    /// Error code for programmatic handling.
    /// </summary>
    public string ErrorCode { get; init; }

    /// <summary>
    /// Diagnostic context information.
    /// </summary>
    public ErrorContext ErrorContext { get; init; }

    /// <summary>
    /// Exit code from the command execution.
    /// </summary>
    public int ExitCode { get; init; }

    /// <summary>
    /// Standard output from the command.
    /// </summary>
    public string Output { get; init; }

    /// <summary>
    /// Creates a successful command response.
    /// </summary>
    public static CommandResponse<T> Ok(T data)
    {
      return new CommandResponse<T>
      {
        Success = true,
        Data = data,
        ExitCode = 0
      };
    }

    /// <summary>
    /// Creates a successful command response with output.
    /// </summary>
    public static CommandResponse<T> Ok(T data, string output)
    {
      return new CommandResponse<T>
      {
        Success = true,
        Data = data,
        Output = output,
        ExitCode = 0
      };
    }

    /// <summary>
    /// Creates a failed command response.
    /// </summary>
    public static CommandResponse<T> Fail(string error, string errorCode = null, int exitCode = -1)
    {
      return new CommandResponse<T>
      {
        Success = false,
        Error = error,
        ErrorCode = errorCode ?? ErrorCodes.General.Unknown,
        ExitCode = exitCode
      };
    }

    /// <summary>
    /// Creates a failed command response with error context.
    /// </summary>
    public static CommandResponse<T> Fail(string error, string errorCode, ErrorContext context, int exitCode = -1)
    {
      return new CommandResponse<T>
      {
        Success = false,
        Error = error,
        ErrorCode = errorCode,
        ErrorContext = context,
        ExitCode = exitCode
      };
    }
  }
#pragma warning restore CA1000
}
